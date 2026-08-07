using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Bir stash girdisi (P07-T12).
/// </summary>
public sealed record StashEntry
{
    /// <summary>Seçici — <c>refs/stash@{0}</c>.</summary>
    public required string Selector { get; init; }

    /// <summary>Stash commit'inin tam SHA'sı.</summary>
    public required string ObjectId { get; init; }

    /// <summary>Girdinin mesajı — <c>On main: benim stash</c>.</summary>
    public required string Message { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Sıra numarası — <c>stash@{N}</c> içindeki N.</summary>
    public int Index { get; init; }

    /// <summary>
    /// Takip edilmeyen dosyalar da bu stash'e dahil mi?
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: <c>-u</c> ile alınan stash'in <b>üçüncü bir ebeveyni</b> var — takip
    /// edilmeyen dosyaların commit'i. Ayrım buradan yapılıyor; mesaja bakmak
    /// (kullanıcı mesajı serbestçe yazdığı için) güvenilmez olurdu.
    /// </remarks>
    public bool IncludesUntracked { get; init; }

    /// <summary>Kısa gösterim için <c>stash@{N}</c>.</summary>
    public string ShortSelector =>
        $"stash@{{{Index.ToString(CultureInfo.InvariantCulture)}}}";
}

/// <summary>Stash oluşturma seçenekleri (P07-T12).</summary>
public sealed record StashPushOptions
{
    /// <summary>Girdinin mesajı.</summary>
    public string? Message { get; init; }

    /// <summary><c>--include-untracked</c>.</summary>
    public bool IncludeUntracked { get; init; }

    /// <summary><c>--keep-index</c>: stage'lenmiş olanlar çalışma ağacında kalsın.</summary>
    public bool KeepIndex { get; init; }

    /// <summary>Yalnızca bu yollar; boşsa tamamı.</summary>
    public IReadOnlyList<RepositoryPath> Paths { get; init; } = [];
}

/// <summary>Stash uygulama sonucu (P07-T12).</summary>
public sealed record StashApplyResult
{
    public required bool HasConflicts { get; init; }

    public IReadOnlyList<RepositoryPath> ConflictedPaths { get; init; } = [];

    /// <summary>
    /// Girdi listede kaldı mı?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — <c>pop</c> çakışırsa girdi DÜŞMÜYOR</b>
    /// (<i>"The stash entry is kept in case you need it again."</i>, rc=1). Kullanıcıya
    /// söylenmezse ya değişikliği iki kez uygular ya da elle silerken kaybeder.
    /// </remarks>
    public required bool EntryKept { get; init; }

    /// <summary>
    /// Stage'lenmiş/stage'lenmemiş ayrımı korunabildi mi?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — <c>pop</c> varsayılan hâlde bu ayrımı sessizce KAYBEDİYOR.</b>
    /// Bir dosya stage'li, biri değilken pop sonrası <b>ikisi de</b> stage'siz oluyor.
    /// <c>--index</c> ile ayrım korunuyor; ama <c>--index</c> her durumda uygulanamıyor
    /// (çakışma varken git reddediyor), o yüzden sonuç raporlanıyor.
    /// </remarks>
    public required bool IndexRestored { get; init; }
}

/// <summary>Stash işlemleri (P07-T12).</summary>
public interface IStashWriter
{
    Task<IReadOnlyList<StashEntry>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <returns>Kenara konacak bir şey yoksa <see langword="false"/>.</returns>
    Task<bool> PushAsync(
        string workingDirectory,
        StashPushOptions options,
        CancellationToken cancellationToken = default);

    Task<StashApplyResult> ApplyAsync(
        string workingDirectory,
        string selector,
        bool drop,
        CancellationToken cancellationToken = default);

    Task DropAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default);

    /// <summary>Stash'i yeni bir dala açar (<c>git stash branch</c>).</summary>
    Task BranchAsync(
        string workingDirectory,
        string selector,
        string branchName,
        CancellationToken cancellationToken = default);

    /// <summary>Girdinin diff'ini üretir.</summary>
    Task<string> ShowAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git stash</c> sarmalayıcısı (P07-T12).
/// </summary>
public sealed class StashWriter : IStashWriter
{
    private const string Format = "%x1e%gD%x00%H%x00%ct%x00%gs%x00%P";

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public StashWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<IReadOnlyList<StashEntry>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "stash", "list", $"--format={Format}"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    /// <summary>NUL ayrılmış <c>stash list</c> çıktısını ayrıştırır.</summary>
    /// <remarks>
    /// Ayırıcı NUL: stash mesajını <b>kullanıcı</b> yazıyor ve içinde sekme olabilir
    /// (P07-T14'te reflog'da ölçülen aynı tuzak).
    /// </remarks>
    internal static IReadOnlyList<StashEntry> Parse(string output)
    {
        List<StashEntry> entries = [];
        int index = 0;

        foreach (string record in output.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Trim('\n', '\r').Split('\0');

            if (fields.Length < 5 || fields[0].Length == 0)
            {
                continue;
            }

            // Ebeveynler: <HEAD> <index-commit> [<untracked-commit>]
            int parents = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            entries.Add(new StashEntry
            {
                Selector = fields[0],
                ObjectId = fields[1],
                Timestamp = long.TryParse(fields[2], CultureInfo.InvariantCulture, out long seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : default,
                Message = fields[3],
                Index = index++,
                IncludesUntracked = parents >= 3,
            });
        }

        return entries;
    }

    public async Task<bool> PushAsync(
        string workingDirectory,
        StashPushOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> arguments = ["stash", "push"];

        if (options.IncludeUntracked)
        {
            arguments.Add("--include-untracked");
        }

        if (options.KeepIndex)
        {
            arguments.Add("--keep-index");
        }

        if (options.Message is { Length: > 0 } message)
        {
            arguments.Add("-m");
            arguments.Add(message);
        }

        if (options.Paths.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(options.Paths.Select(path => path.Value));
        }

        GitResult result = await _writer
            .RunAsync(workingDirectory, arguments, cancellationToken)
            .ConfigureAwait(false);

        // Kenara konacak bir şey yoksa git "No local changes to save" deyip 0 dönüyor.
        // Bunu "stash'lendi" diye raporlamak, kullanıcının olmayan bir girdiyi
        // aramasına yol açardı.
        return !result.GetStandardOutputText()
            .Contains("No local changes", StringComparison.Ordinal);
    }

    /// <remarks>
    /// <c>--index</c> <b>önce</b> deneniyor: ölçümde varsayılan <c>pop</c> stage'lenmiş
    /// olanı stage'siz bırakıyordu. <c>--index</c> her durumda uygulanamıyor (çakışma
    /// varken git reddediyor), o yüzden başarısızlıkta sade biçime düşülüyor ve
    /// <see cref="StashApplyResult.IndexRestored"/> ile <b>ne olduğu söyleniyor</b>.
    /// </remarks>
    public async Task<StashApplyResult> ApplyAsync(
        string workingDirectory,
        string selector,
        bool drop,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        string verb = drop ? "pop" : "apply";
        bool indexRestored = true;
        GitException? failure = null;

        try
        {
            await _writer
                .RunAsync(workingDirectory, ["stash", verb, "--index", selector], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException)
        {
            indexRestored = false;

            try
            {
                await _writer
                    .RunAsync(workingDirectory, ["stash", verb, selector], cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GitException error)
            {
                // Çakışma da buraya düşüyor; gerçek hata mı çakışma mı, index söyleyecek.
                failure = error;
            }
        }

        IReadOnlyList<RepositoryPath> conflicts =
            await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        if (failure is not null && conflicts.Count == 0)
        {
            // Çakışma yoksa bu gerçek bir hataydı (bilinmeyen seçici, kirli ağaç…);
            // sessizce "oldu" demek yanlış olurdu.
            throw failure;
        }

        IReadOnlyList<StashEntry> remaining =
            await ListAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        return new StashApplyResult
        {
            HasConflicts = conflicts.Count > 0,
            ConflictedPaths = conflicts,
            EntryKept = !drop || remaining.Any(entry =>
                string.Equals(entry.Selector, selector, StringComparison.Ordinal)
                || conflicts.Count > 0),
            IndexRestored = indexRestored,
        };
    }

    public Task DropAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default) =>
        _writer.RunAsync(workingDirectory, ["stash", "drop", selector], cancellationToken);

    public Task BranchAsync(
        string workingDirectory,
        string selector,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        return _writer.RunAsync(
            workingDirectory,
            ["stash", "branch", branchName, selector],
            cancellationToken);
    }

    public async Task<string> ShowAsync(
        string workingDirectory,
        string selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory,
                "stash", "show", "--patch", "--no-color", selector),
            cancellationToken).ConfigureAwait(false);

        // Kayıpsız okunuyor: diff içeriği deponun kendi baytları, tek bir kodlamada değil
        // (P04'ün dersi).
        return result.IsSuccess ? result.GetStandardOutputLossless() : string.Empty;
    }

    private async Task<IReadOnlyList<RepositoryPath>> ReadConflictsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return [];
        }

        List<RepositoryPath> paths = [];

        foreach (string value in result.GetStandardOutputText()
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (RepositoryPath.TryParse(value, out RepositoryPath path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}
