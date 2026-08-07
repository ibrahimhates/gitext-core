using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

// ===================================================== P07-T17 dosya geçmişi

/// <summary>Bir dosyanın geçmişindeki tek girdi (P07-T17).</summary>
public sealed record FileHistoryEntry
{
    public required string ObjectId { get; init; }

    public required string Subject { get; init; }

    public string AuthorName { get; init; } = string.Empty;

    public DateTimeOffset AuthorTime { get; init; }

    /// <summary>
    /// Dosyanın o commit'teki adı.
    /// </summary>
    /// <remarks>
    /// Yeniden adlandırma boyunca takipte bu değişiyor; ekranda "şu ada sahipti"
    /// gösterilebilsin diye tutuluyor.
    /// </remarks>
    public string Path { get; init; } = string.Empty;

    /// <summary>Bu commit'te dosya yeniden adlandırılmış mı?</summary>
    public bool IsRename { get; init; }

    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;
}

/// <summary>Dosya geçmişi okuma (P07-T17).</summary>
public interface IFileHistoryReader
{
    Task<IReadOnlyList<FileHistoryEntry>> ReadAsync(
        string workingDirectory,
        RepositoryPath path,
        int limit = 200,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git log --follow</c> okuyucusu (P07-T17).
/// </summary>
/// <remarks>
/// <b>ÖLÇÜLDÜ — <c>--follow</c> gerçekten fark yaratıyor.</b> Bir kez yeniden adlandırılmış
/// dosyada <c>--follow</c> ile 3 commit, onsuz <b>1</b> commit görünüyordu: yeniden
/// adlandırmadan önceki geçmiş tamamen kayboluyor. Kullanıcı "bu dosyanın geçmişi bu kadar
/// mıymış" diye düşünürdü.
/// </remarks>
public sealed class FileHistoryReader : IFileHistoryReader
{
    /// <summary>Kayıt ayracı <b>başta</b>.</summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — ayraç sonda olursa <c>--name-status</c> satırları yanlış kayda
    /// düşüyor.</b> git bu satırları biçim çıktısının <b>ardından</b> yazıyor; ayraç sonda
    /// olduğunda bölme sonucu her parça, bir <b>önceki</b> commit'in durum satırlarıyla
    /// başlıyordu. Sonuç: yeniden adlandırma bir sonraki commit'e atfedilirdi. Ayraç başa
    /// alınınca her parça kendi durum satırlarını taşıyor.
    /// </remarks>
    private const string Format = "%x1e%H%x00%s%x00%an%x00%at";

    private readonly IGitProcessRunner _runner;

    public FileHistoryReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<FileHistoryEntry>> ReadAsync(
        string workingDirectory,
        RepositoryPath path,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (path.IsEmpty)
        {
            return [];
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory,
                "log",
                "--follow",
                "--name-status",
                $"--format={Format}",
                $"--max-count={limit.ToString(CultureInfo.InvariantCulture)}",
                "--",
                path.Value),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText(), path.Value) : [];
    }

    /// <remarks>
    /// Her kayıt: <c>&lt;sha&gt;\0&lt;konu&gt;\0&lt;yazar&gt;\0&lt;zaman&gt;</c>, ardından
    /// <c>--name-status</c> satırları — <c>R100&lt;TAB&gt;eski&lt;TAB&gt;yeni</c> ya da
    /// <c>M&lt;TAB&gt;yol</c>. Yeniden adlandırmalar buradan okunuyor.
    /// </remarks>
    internal static IReadOnlyList<FileHistoryEntry> Parse(string output, string currentPath)
    {
        List<FileHistoryEntry> entries = [];

        foreach (string record in output.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Split('\0');

            if (fields.Length < 4 || fields[0].Length == 0)
            {
                continue;
            }

            // Son alan: zaman damgası + ardından gelen durum satırları.
            string[] tail = fields[3].Split('\n', StringSplitOptions.RemoveEmptyEntries);

            string path = currentPath;
            bool rename = false;

            foreach (string status in tail.Skip(1))
            {
                string[] columns = status.Split('\t');

                if (columns.Length >= 3 && columns[0].StartsWith('R'))
                {
                    // Yeniden adlandırmada ESKİ ad ilginç olan: "bu dosya eskiden şuydu".
                    rename = true;
                    path = columns[1];
                    break;
                }

                if (columns.Length >= 2)
                {
                    path = columns[1];
                }
            }

            entries.Add(new FileHistoryEntry
            {
                ObjectId = fields[0],
                Subject = fields[1],
                AuthorName = fields[2],
                AuthorTime =
                    long.TryParse(tail.FirstOrDefault(), CultureInfo.InvariantCulture, out long seconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                        : default,
                Path = path,
                IsRename = rename,
            });
        }

        return entries;
    }
}

// ============================================================ P07-T18 tag

/// <summary>Bir etiket (P07-T18).</summary>
public sealed record GitTag
{
    public required string Name { get; init; }

    /// <summary>Etiketin gösterdiği commit.</summary>
    public required string ObjectId { get; init; }

    /// <summary>
    /// Açıklamalı (annotated) etiket mi?
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: ayrım <c>%(objecttype)</c> ile yapılıyor — açıklamalıda <c>tag</c>,
    /// hafifte <c>commit</c>. Açıklamalıda <c>%(*objectname)</c> asıl commit'i veriyor;
    /// <c>%(objectname)</c> <b>etiket nesnesinin</b> kendi SHA'sı, commit değil.
    /// </remarks>
    public bool IsAnnotated { get; init; }

    public string Message { get; init; } = string.Empty;

    public string TaggerName { get; init; } = string.Empty;

    public DateTimeOffset? TaggedAt { get; init; }
}

/// <summary>Etiket oluşturma seçenekleri (P07-T18).</summary>
public sealed record TagOptions
{
    public required string Name { get; init; }

    /// <summary>Etiketlenecek commit; <see langword="null"/> ise <c>HEAD</c>.</summary>
    public string? Target { get; init; }

    /// <summary>Açıklama metni; verilirse etiket <b>annotated</b> olur.</summary>
    public string? Message { get; init; }

    /// <summary><c>--sign</c>: GPG/SSH ile imzala.</summary>
    public bool Sign { get; init; }

    /// <summary><c>--force</c>: aynı adlı etiketi taşı.</summary>
    public bool Force { get; init; }
}

/// <summary>Etiket işlemleri (P07-T18).</summary>
public interface ITagWriter
{
    Task<IReadOnlyList<GitTag>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        string workingDirectory,
        TagOptions options,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary><c>git tag</c> sarmalayıcısı (P07-T18).</summary>
public sealed class TagWriter : ITagWriter
{
    /// <remarks>
    /// 🔴 <b><c>for-each-ref</c> <c>%x1e</c>'yi DESTEKLEMİYOR</b> — ölçümde kaçış dizisi
    /// harfi harfine <c>%x1e</c> olarak basıldı (<c>log</c> tabanlı komutlarda çalışıyor).
    /// Burada kayıtlar <b>satır sonuyla</b> ayrılıyor; bu güvenli, çünkü etiket adı,
    /// nesne adı ve <c>contents:subject</c> tek satır.
    /// <para>
    /// Alanlar yine NUL: hafif bir etiketin <c>*objectname</c>/<c>taggername</c> alanları
    /// <b>boş</b> geliyor ve NUL çifti kullanılsaydı sahte bir kayıt sınırı üretirdi.
    /// </para>
    /// </remarks>
    private const string Format =
        "%(refname:short)%00%(objecttype)%00%(objectname)%00%(*objectname)%00"
        + "%(contents:subject)%00%(taggerdate:unix)%00%(taggername)";

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public TagWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<IReadOnlyList<GitTag>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "for-each-ref", $"--format={Format}", "refs/tags"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    internal static IReadOnlyList<GitTag> Parse(string output)
    {
        List<GitTag> tags = [];

        foreach (string record in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.TrimEnd('\r').Split('\0');

            if (fields.Length < 7 || fields[0].Length == 0)
            {
                continue;
            }

            bool annotated = string.Equals(fields[1], "tag", StringComparison.Ordinal);

            tags.Add(new GitTag
            {
                Name = fields[0],
                IsAnnotated = annotated,

                // Açıklamalıda `%(objectname)` ETİKET NESNESİNİN SHA'sı; commit
                // `%(*objectname)`de. Karıştırmak, etikete tıklayınca var olmayan bir
                // commit'e gitmek demekti.
                ObjectId = annotated && fields[3].Length > 0 ? fields[3] : fields[2],
                Message = fields[4],
                TaggedAt = long.TryParse(fields[5], CultureInfo.InvariantCulture, out long seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : null,
                TaggerName = fields[6],
            });
        }

        return tags;
    }

    public Task CreateAsync(
        string workingDirectory,
        TagOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);

        List<string> arguments = ["tag"];

        if (options.Force)
        {
            arguments.Add("--force");
        }

        if (options.Sign)
        {
            arguments.Add("--sign");
        }

        if (options.Message is { Length: > 0 } message)
        {
            arguments.Add("--annotate");
            arguments.Add("-m");
            arguments.Add(message);
        }

        arguments.Add(options.Name);

        if (options.Target is { Length: > 0 } target)
        {
            arguments.Add(target);
        }

        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }

    public Task DeleteAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _writer.RunAsync(workingDirectory, ["tag", "--delete", name], cancellationToken);
    }
}

// ======================================================== P07-T20 worktree

/// <summary>Bağlı bir çalışma ağacı (P07-T20).</summary>
public sealed record WorkTree
{
    public required string Path { get; init; }

    public string ObjectId { get; init; } = string.Empty;

    /// <summary>Üzerindeki dal; ayrık <c>HEAD</c> ise <see langword="null"/>.</summary>
    public string? BranchName { get; init; }

    /// <summary>Ana çalışma ağacı mı? (Listenin ilki.)</summary>
    public bool IsMain { get; init; }

    public bool IsDetached => BranchName is null;

    /// <summary>Kilitli mi? Kilitli worktree kaldırılamaz.</summary>
    public bool IsLocked { get; init; }

    /// <summary>Dizini artık yok mu?</summary>
    public bool IsPrunable { get; init; }
}

/// <summary>Worktree işlemleri (P07-T20).</summary>
public interface IWorkTreeReader
{
    Task<IReadOnlyList<WorkTree>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        string workingDirectory,
        string path,
        string? branch,
        bool createBranch,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string workingDirectory,
        string path,
        bool force,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git worktree</c> sarmalayıcısı (P07-T20).
/// </summary>
/// <remarks>
/// <c>--porcelain</c> çıktısı <b>boş satırla ayrılmış bloklar</b>: her blok
/// <c>worktree &lt;yol&gt;</c> ile başlıyor, ardından <c>HEAD</c>, <c>branch</c>,
/// <c>detached</c>, <c>locked</c>, <c>prunable</c> gibi anahtarlar geliyor.
/// </remarks>
public sealed class WorkTreeReader : IWorkTreeReader
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public WorkTreeReader(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<IReadOnlyList<WorkTree>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "worktree", "list", "--porcelain"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    internal static IReadOnlyList<WorkTree> Parse(string output)
    {
        List<WorkTree> trees = [];

        foreach (string block in output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string path = string.Empty;
            string objectId = string.Empty;
            string? branch = null;
            bool locked = false;
            bool prunable = false;

            foreach (string raw in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.TrimEnd('\r');
                int space = line.IndexOf(' ', StringComparison.Ordinal);
                string key = space < 0 ? line : line[..space];
                string value = space < 0 ? string.Empty : line[(space + 1)..];

                switch (key)
                {
                    case "worktree":
                        path = value;
                        break;
                    case "HEAD":
                        objectId = value;
                        break;
                    case "branch":
                        // `refs/heads/main` → `main`
                        branch = value.StartsWith("refs/heads/", StringComparison.Ordinal)
                            ? value["refs/heads/".Length..]
                            : value;
                        break;
                    case "locked":
                        locked = true;
                        break;
                    case "prunable":
                        prunable = true;
                        break;
                    default:
                        break;
                }
            }

            if (path.Length == 0)
            {
                continue;
            }

            trees.Add(new WorkTree
            {
                Path = path,
                ObjectId = objectId,
                BranchName = branch,

                // git ana çalışma ağacını her zaman ilk yazıyor.
                IsMain = trees.Count == 0,
                IsLocked = locked,
                IsPrunable = prunable,
            });
        }

        return trees;
    }

    public Task AddAsync(
        string workingDirectory,
        string path,
        string? branch,
        bool createBranch,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        List<string> arguments = ["worktree", "add"];

        if (createBranch && branch is { Length: > 0 } created)
        {
            arguments.Add("-b");
            arguments.Add(created);
        }

        arguments.Add(path);

        if (!createBranch && branch is { Length: > 0 } existing)
        {
            arguments.Add(existing);
        }

        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }

    /// <remarks>
    /// ⚠️ <c>--force</c> yalnızca kullanıcı açıkça isterse: kirli bir worktree'yi zorla
    /// kaldırmak commit'lenmemiş işi siler.
    /// </remarks>
    public Task RemoveAsync(
        string workingDirectory,
        string path,
        bool force,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        List<string> arguments = ["worktree", "remove"];

        if (force)
        {
            arguments.Add("--force");
        }

        arguments.Add(path);
        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }
}

// ======================================================= P07-T21 arama

/// <summary>Commit araması ölçütü (P07-T21).</summary>
public sealed record CommitSearchQuery
{
    /// <summary><c>--grep</c>: commit mesajında ara.</summary>
    public string? Message { get; init; }

    /// <summary><c>--author</c>.</summary>
    public string? Author { get; init; }

    /// <summary>
    /// <c>-S</c> (pickaxe): bu metnin <b>geçtiği sayısı</b> değişen commit'ler.
    /// </summary>
    /// <remarks>
    /// <c>-S</c> ile <c>-G</c> farkı ince ama önemli: <c>-S</c> "bu dizgenin kaç kez
    /// geçtiği değişti mi" diye bakıyor (yani eklendi ya da silindi), <c>-G</c> ise
    /// "diff'in kendisi bu düzenli ifadeyle eşleşiyor mu". Bir satırın taşınması
    /// <c>-S</c>'te görünmez, <c>-G</c>'de görünür.
    /// </remarks>
    public string? ContentAdded { get; init; }

    /// <summary><c>-G</c>: diff metnine düzenli ifade uygula.</summary>
    public string? ContentPattern { get; init; }

    /// <summary>Aramayı bu yollarla sınırla.</summary>
    public IReadOnlyList<RepositoryPath> Paths { get; init; } = [];

    /// <summary>Büyük/küçük harf duyarsız ara.</summary>
    public bool IgnoreCase { get; init; } = true;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Message)
        && string.IsNullOrWhiteSpace(Author)
        && string.IsNullOrWhiteSpace(ContentAdded)
        && string.IsNullOrWhiteSpace(ContentPattern);
}

/// <summary>Dosya içeriğinde bulunan bir eşleşme (P07-T21).</summary>
public sealed record ContentMatch
{
    public required string Path { get; init; }

    public required int LineNumber { get; init; }

    public required string Line { get; init; }
}

/// <summary>Arama (P07-T21).</summary>
public interface ISearchReader
{
    /// <summary>Commit'lerde arar.</summary>
    Task<IReadOnlyList<string>> SearchCommitsAsync(
        string workingDirectory,
        CommitSearchQuery query,
        int limit = 500,
        CancellationToken cancellationToken = default);

    /// <summary>Çalışma ağacındaki dosya içeriklerinde arar (<c>git grep</c>).</summary>
    Task<IReadOnlyList<ContentMatch>> SearchContentAsync(
        string workingDirectory,
        string pattern,
        bool ignoreCase = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Commit ve içerik araması (P07-T21).</summary>
public sealed class SearchReader : ISearchReader
{
    private readonly IGitProcessRunner _runner;

    public SearchReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<string>> SearchCommitsAsync(
        string workingDirectory,
        CommitSearchQuery query,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (query.IsEmpty)
        {
            // Boş sorgu tüm geçmişi döndürürdü; "arama yapılmadı" demek daha dürüst.
            return [];
        }

        List<string> arguments =
        [
            "log",
            "--format=%H",
            $"--max-count={limit.ToString(CultureInfo.InvariantCulture)}",
        ];

        if (query.IgnoreCase)
        {
            arguments.Add("--regexp-ignore-case");
        }

        if (query.Message is { Length: > 0 } message)
        {
            arguments.Add($"--grep={message}");
        }

        if (query.Author is { Length: > 0 } author)
        {
            arguments.Add($"--author={author}");
        }

        if (query.ContentAdded is { Length: > 0 } added)
        {
            arguments.Add($"-S{added}");
        }

        if (query.ContentPattern is { Length: > 0 } pattern)
        {
            arguments.Add($"-G{pattern}");
        }

        if (query.Paths.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(query.Paths.Select(path => path.Value));
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, [.. arguments]),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? [.. result.GetStandardOutputText().Split('\n', StringSplitOptions.RemoveEmptyEntries)]
            : [];
    }

    /// <remarks>
    /// <c>-z</c> ile alanlar NUL ayrılıyor: yol boşluk ve iki nokta içerebilir, satır
    /// içeriği de öyle. <c>-z</c> olmadan <c>yol:satır:içerik</c> ayrıştırması, iki nokta
    /// içeren bir yolda sessizce kayardı.
    /// </remarks>
    public async Task<IReadOnlyList<ContentMatch>> SearchContentAsync(
        string workingDirectory,
        string pattern,
        bool ignoreCase = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        List<string> arguments = ["grep", "--line-number", "--no-color", "-z"];

        if (ignoreCase)
        {
            arguments.Add("--ignore-case");
        }

        arguments.Add("-e");
        arguments.Add(pattern);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = arguments,

                // Eşleşme yoksa `git grep` çıkış kodu 1 veriyor; bu bir hata değil.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return Parse(result.GetStandardOutputLossless());
    }

    /// <summary><c>git grep -z --line-number</c> çıktısını ayrıştırır.</summary>
    internal static IReadOnlyList<ContentMatch> Parse(string output)
    {
        List<ContentMatch> matches = [];

        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // `<yol>\0<satır no>\0<içerik>` — içerikte NUL olamaz.
            string[] fields = line.Split('\0', 3);

            if (fields.Length < 3
                || !int.TryParse(fields[1], CultureInfo.InvariantCulture, out int number))
            {
                continue;
            }

            matches.Add(new ContentMatch
            {
                Path = fields[0],
                LineNumber = number,
                Line = fields[2],
            });
        }

        return matches;
    }
}
