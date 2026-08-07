using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Commit'leri yeniden oynatan işlem türü (P07-T07, P07-T08).
/// </summary>
/// <remarks>
/// <c>cherry-pick</c> ve <c>revert</c> git içinde <b>aynı sequencer</b> tarafından
/// yürütülüyor: aynı durum dosyaları, aynı <c>--continue</c>/<c>--skip</c>/<c>--abort</c>
/// üçlüsü, aynı çakışma davranışı. Bu yüzden tek bir yazıcı ile ele alınıyorlar.
/// </remarks>
public enum SequencerOperation
{
    /// <summary>Commit'i buraya uygula.</summary>
    CherryPick,

    /// <summary>Commit'in yaptığını geri alan yeni bir commit üret.</summary>
    Revert,
}

/// <summary>Cherry-pick / revert seçenekleri (P07-T07, P07-T08).</summary>
public sealed record SequencerOptions
{
    public required SequencerOperation Operation { get; init; }

    /// <summary>Uygulanacak commit'ler — verildikleri sırayla.</summary>
    public required IReadOnlyList<string> Commits { get; init; }

    /// <summary>
    /// <c>--no-commit</c>: değişiklikleri hazırla ama commit'leme.
    /// </summary>
    /// <remarks>
    /// Çoklu commit ile birlikte kullanıldığında hepsi tek bir hazırlığa yığılır.
    /// </remarks>
    public bool NoCommit { get; init; }

    /// <summary>
    /// <c>-x</c>: mesaja <i>"(cherry picked from commit …)"</i> satırı ekler.
    /// </summary>
    /// <remarks>
    /// Yalnızca cherry-pick için anlamlı; revert zaten kaynağı mesaja yazıyor.
    /// </remarks>
    public bool RecordOrigin { get; init; }

    /// <summary>
    /// Merge commit'i için hangi ebeveynin "ana hat" sayılacağı (1 tabanlı).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — merge commit'ini <c>-m</c> olmadan revert etmek rc=128 veriyor</b>
    /// (<c>is a merge but no -m option was given</c>). Hangi ebeveyne göre geri alınacağı
    /// git'in tahmin edebileceği bir şey değil; kullanıcı seçmeli.
    /// </remarks>
    public int? MainlineParent { get; init; }
}

/// <summary>Cherry-pick / revert sonucu (P07-T07, P07-T08).</summary>
public sealed record SequencerResult
{
    public required SequencerOperation Operation { get; init; }

    /// <summary>İşlem öncesi konum — geri alma bilgisi bunun üzerinden.</summary>
    public required SafetyPoint SafetyPoint { get; init; }

    /// <summary>Çakışmayla durdu mu?</summary>
    public bool HasConflicts => ConflictedPaths.Count > 0;

    public IReadOnlyList<RepositoryPath> ConflictedPaths { get; init; } = [];

    /// <summary>Oluşan commit sayısı.</summary>
    public int CommitsCreated { get; init; }

    /// <summary>
    /// Kullanıcının hâlâ commit'lemesi gerekiyor mu?
    /// </summary>
    /// <remarks>
    /// <c>--no-commit</c> "başarılı" dönüyor ama <c>HEAD</c> ilerlemiyor — P06-T11'de
    /// <c>--squash</c> ile aynı tuzak.
    /// </remarks>
    public bool RequiresCommit { get; init; }
}

/// <summary>Cherry-pick ve revert (P07-T07, P07-T08).</summary>
public interface ISequencerWriter
{
    Task<SequencerResult> RunAsync(
        string workingDirectory,
        SequencerOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Verilen commit bir merge commit'i mi? (<c>-m</c> gerekiyor mu?)</summary>
    Task<int> CountParentsAsync(
        string workingDirectory,
        string commit,
        CancellationToken cancellationToken = default);

    string DescribeCommand(SequencerOptions options);
}

/// <summary>
/// <c>git cherry-pick</c> ve <c>git revert</c> sarmalayıcısı (P07-T07, P07-T08).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Çakışma bir hata değil, bir DURUM.</b> Her ikisi de çakışmada rc=1 veriyor ve
/// metni <c>stdout</c>'a yazıyor (P06-T11'de merge'de, P06-T07'de pull'da aynı ders).
/// Karar metne değil <b>index'e</b> bakarak veriliyor: <c>diff --diff-filter=U</c>.
/// </para>
/// <para>
/// ÖLÇÜLDÜ — çakışmada bırakılan durum: <c>.git/CHERRY_PICK_HEAD</c> (ya da
/// <c>REVERT_HEAD</c>) + <c>MERGE_MSG</c>. Çoklu commit'te ayrıca <c>.git/sequencer/</c>.
/// Çözüm akışı P07-T05'e bağlanıyor.
/// </para>
/// </remarks>
public sealed class SequencerWriter : ISequencerWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly ISafetyPointRecorder _safety;

    public SequencerWriter(IGitWriter writer, IGitProcessRunner runner, ISafetyPointRecorder safety)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(safety);

        _writer = writer;
        _runner = runner;
        _safety = safety;
    }

    public async Task<SequencerResult> RunAsync(
        string workingDirectory,
        SequencerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Commits.Count == 0)
        {
            throw new ArgumentException("En az bir commit gerekli.", nameof(options));
        }

        SafetyPoint point = await _safety
            .CaptureAsync(workingDirectory, Verb(options.Operation), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _writer
                .RunAsync(workingDirectory, BuildArguments(options), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException)
        {
            IReadOnlyList<RepositoryPath> conflicts =
                await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

            // Gerçek hatalar (bilinmeyen commit, kirli ağaç, eksik -m) olduğu gibi yukarı.
            if (conflicts.Count == 0)
            {
                throw;
            }

            return new SequencerResult
            {
                Operation = options.Operation,
                SafetyPoint = point,
                ConflictedPaths = conflicts,
            };
        }

        string after = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        bool moved = !string.Equals(point.ObjectId, after, StringComparison.Ordinal);

        int created = moved
            ? await CountBetweenAsync(workingDirectory, point.ObjectId, after, cancellationToken)
                .ConfigureAwait(false)
            : 0;

        return new SequencerResult
        {
            Operation = options.Operation,
            SafetyPoint = point,
            CommitsCreated = created,
            RequiresCommit = !moved
                && await HasStagedChangesAsync(workingDirectory, cancellationToken).ConfigureAwait(false),
        };
    }

    public async Task<int> CountParentsAsync(
        string workingDirectory,
        string commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--parents", "-1", commit),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return 0;
        }

        // "<commit> <ebeveyn1> <ebeveyn2>…" — ilk alan commit'in kendisi.
        return Math.Max(
            0,
            result.GetStandardOutputText().Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length - 1);
    }

    public string DescribeCommand(SequencerOptions options) => Describe(options);

    /// <summary>Çalıştırılacak komutu üretir ("komutu göster" ilkesi).</summary>
    public static string Describe(SequencerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return "git " + string.Join(' ', BuildArguments(options));
    }

    internal static string Verb(SequencerOperation operation) =>
        operation == SequencerOperation.Revert ? "revert" : "cherry-pick";

    private static IReadOnlyList<string> BuildArguments(SequencerOptions options)
    {
        List<string> arguments = [Verb(options.Operation)];

        if (options.MainlineParent is { } mainline)
        {
            arguments.Add("-m");
            arguments.Add(mainline.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (options.NoCommit)
        {
            arguments.Add("--no-commit");
        }

        if (options.RecordOrigin && options.Operation == SequencerOperation.CherryPick)
        {
            arguments.Add("-x");
        }

        // Revert kendi mesajını üretebiliyor; editör açtırmamak için --no-edit.
        // (Cherry-pick zaten kaynak mesajı kullanıyor ve editör açmıyor.)
        if (options.Operation == SequencerOperation.Revert && !options.NoCommit)
        {
            arguments.Add("--no-edit");
        }

        arguments.AddRange(options.Commits);
        return arguments;
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

    private async Task<string> ReadHeadAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "HEAD"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? result.GetStandardOutputText().Trim() : string.Empty;
    }

    private async Task<int> CountBetweenAsync(
        string workingDirectory,
        string from,
        string to,
        CancellationToken cancellationToken)
    {
        if (from.Length == 0)
        {
            return 0;
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--count", $"{from}..{to}"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
               && int.TryParse(
                   result.GetStandardOutputText().Trim(),
                   System.Globalization.CultureInfo.InvariantCulture,
                   out int count)
            ? count
            : 0;
    }

    private async Task<bool> HasStagedChangesAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // `--quiet` fark varsa 1 döner; bu bir hata değil (P02'de beyan edilen kalıp).
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["diff", "--cached", "--quiet"],
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 1;
    }
}
