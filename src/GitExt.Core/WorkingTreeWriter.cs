using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Değişiklikleri geri almanın kapsamı (P05-T08).
/// </summary>
public enum DiscardScope
{
    /// <summary>
    /// Yalnızca stage'lenmemiş değişiklikler atılır; index'teki içerik korunur.
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> düz <c>git restore</c> çalışma ağacını <b>index'ten</b> geri yüklüyor,
    /// HEAD'den değil. Yani stage'lenmiş bir değişiklik ayakta kalıyor. Bu, "değişikliği geri
    /// al" düğmesinin çoğu kullanıcı için beklediği davranış.
    /// </remarks>
    UnstagedOnly,

    /// <summary>Hem stage'lenmiş hem stage'lenmemiş değişiklikler atılır (HEAD'e döner).</summary>
    All,
}

/// <summary>
/// <c>git clean</c> kapsamı (P05-T08).
/// </summary>
public sealed record CleanOptions
{
    public static CleanOptions Default { get; } = new();

    /// <summary>Takip edilmeyen <b>dizinler</b> de silinsin mi (<c>-d</c>).</summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> <c>-d</c> olmadan takip edilmeyen bir dizin <b>hiç silinmiyor</b> ve
    /// bu bir hata olarak da bildirilmiyor.
    /// </remarks>
    public bool IncludeDirectories { get; init; } = true;

    /// <summary>Yok sayılan (ignored) dosyalar da silinsin mi (<c>-x</c>).</summary>
    /// <remarks>
    /// ⚠️ Tehlikeli: derleme çıktısının yanında <c>.env</c> gibi <b>yeniden üretilemeyen</b>
    /// dosyalar da genellikle yok sayılır.
    /// </remarks>
    public bool IncludeIgnored { get; init; }

    /// <summary>Yalnızca yok sayılan dosyalar silinsin (<c>-X</c>).</summary>
    public bool OnlyIgnored { get; init; }

    /// <summary>İç içe git depoları da silinsin mi (<c>-ff</c>).</summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> tek <c>-f</c> ile iç içe bir depo (klonlanmış alt dizin)
    /// <b>sessizce atlanıyor</b> — çıktıda hiç görünmüyor. Kullanıcı "temizlendi" sanır,
    /// dizin durmaya devam eder.
    /// </remarks>
    public bool IncludeNestedRepositories { get; init; }
}

/// <summary>
/// Atılan içeriğin nesne veritabanına alınmış yedeği (P05-T08).
/// </summary>
/// <remarks>
/// <b>ÖLÇÜLDÜ:</b> <c>git hash-object -w</c> ile yazılan blob, geri alma işleminden sonra
/// <c>git cat-file -p &lt;blob&gt;</c> ile okunabiliyor.
/// <para>
/// ⚠️ <b>Garanti değil.</b> Bu nesneye hiçbir ref işaret etmiyor; <c>git gc --prune=now</c>
/// onu <b>anında</b> siliyor (ölçüldü). Varsayılan <c>gc.pruneExpire</c> iki hafta olduğu için
/// pratikte bir süre duruyor, ama "geri alınabilir" diye sunulamaz — bu yüzden yıkıcı işlem
/// yine de <b>açık onay</b> istiyor.
/// </para>
/// </remarks>
public sealed record DiscardBackup
{
    public required RepositoryPath Path { get; init; }

    /// <summary>Atılan içeriğin blob kimliği.</summary>
    public required string BlobId { get; init; }
}

/// <summary>
/// <c>.gitignore</c>'a ekleme girişiminin sonucu (P05-T08).
/// </summary>
public enum GitIgnoreOutcome
{
    /// <summary>Desen eklendi.</summary>
    Added,

    /// <summary>Yol zaten yok sayılıyordu; dosya değiştirilmedi.</summary>
    AlreadyIgnored,

    /// <summary>
    /// Yol <b>izleniyor</b>; <c>.gitignore</c> bu dosyaya etki etmez.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ:</b> izlenen bir dosyayı <c>.gitignore</c>'a eklemek <b>hiçbir şey
    /// yapmıyor</b> — <c>git status</c> dosyayı göstermeye devam ediyor ve
    /// <c>check-ignore</c> bile eşleşme bildirmiyor. Dosyayı sessizce yazıp "eklendi" demek,
    /// kullanıcıya olmayan bir sonuç vaat etmek olurdu. Önce
    /// <see cref="IStagingWriter.UntrackAsync"/> gerekiyor.
    /// </remarks>
    PathIsTracked,
}

/// <summary>
/// Çalışma ağacındaki dosyalar üzerinde <b>yıkıcı</b> işlemler (P05-T08).
/// </summary>
/// <remarks>
/// Buradaki her işlem kullanıcının <b>henüz kaydedilmemiş</b> emeğini silebilir. CLAUDE.md § 8
/// gereği hepsi açık onay istiyor ve onay bir <b>parametre</b> olarak zorunlu tutuluyor
/// (P05-T02'deki <c>GitLock.Remove</c> ile aynı desen): kuralı yorumda bırakmak, birinin
/// ileride onaysız çağırmasına engel olmaz.
/// </remarks>
public interface IWorkingTreeWriter
{
    /// <summary>
    /// Verilen yollardaki değişiklikleri geri alır.
    /// </summary>
    /// <returns>Atılan içeriğin yedekleri; boş liste, yedeklenecek dosya olmadığını gösterir.</returns>
    Task<IReadOnlyList<DiscardBackup>> DiscardChangesAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        DiscardScope scope,
        bool userConfirmed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takip edilmeyen dosyaları <b>siler</b>.
    /// </summary>
    Task DeleteUntrackedAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        bool userConfirmed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Çalışma ağacının <b>tamamını</b> temizler (<c>git clean</c>).
    /// </summary>
    /// <remarks>
    /// Silinecekler önceden <see cref="IStatusReader"/> ile listelenmeli.
    /// <c>git clean --dry-run</c> çıktısı <b>ayrıştırılmaz</b>: insan-okunur
    /// (<c>Would remove …</c>), <c>-z</c> desteklemiyor ve özel karakterli adları
    /// tırnaklıyor (ölçüldü).
    /// </remarks>
    Task CleanAsync(
        string workingDirectory,
        CleanOptions options,
        bool userConfirmed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir deseni deponun kök <c>.gitignore</c> dosyasına ekler.
    /// </summary>
    Task<GitIgnoreOutcome> AddToGitIgnoreAsync(
        string workingDirectory,
        RepositoryPath path,
        string pattern,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IWorkingTreeWriter"/>
public sealed class WorkingTreeWriter : IWorkingTreeWriter
{
    /// <summary>
    /// Tek <c>hash-object</c> çağrısına konan yol sayısı.
    /// </summary>
    /// <remarks>
    /// Yollar <b>argüman</b> olarak veriliyor, stdin ile değil: <c>--stdin-paths</c> yolları
    /// satır sonuyla ayırıyor ve dosya adı satır sonu içerebiliyor. Argüman listesi sınırsız
    /// olmadığı için parçalanıyor. Ölçüldü: 500 dosya tek çağrıda <b>14 ms</b>.
    /// </remarks>
    private const int BackupBatchSize = 500;

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public WorkingTreeWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<IReadOnlyList<DiscardBackup>> DiscardChangesAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        DiscardScope scope,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(paths);

        RequireConfirmation(
            userConfirmed,
            "Değişiklikleri geri almak kaydedilmemiş içeriği siler ve bunun reflog üzerinden "
            + "bir geri dönüşü yoktur; işlem yalnızca kullanıcının açık onayıyla yapılabilir.");

        if (paths.Count == 0)
        {
            // ⚠️ Boş liste ile `git restore --` deponun TAMAMINI geri alırdı (P05-T03'te
            // `git add -A --` için aynı koruma konmuştu).
            return [];
        }

        IReadOnlyList<DiscardBackup> backups =
            await BackupAsync(workingDirectory, paths, cancellationToken).ConfigureAwait(false);

        List<string> arguments = ["restore"];

        if (scope == DiscardScope.All)
        {
            // `--source=HEAD` şart: `--staged` tek başına da HEAD'i kaynak alıyor ama
            // niyeti açıkça yazmak, ileride `--source` eklemeyi unutma riskini kapatıyor.
            // ⚠️ HEAD yokken git `could not resolve 'HEAD'` ile düşer (ölçüldü) — henüz
            // hiç commit'i olmayan depoda "her şeyi geri al" tanımsız.
            arguments.AddRange(["--source=HEAD", "--staged"]);
        }

        arguments.Add("--worktree");
        arguments.Add("--");
        arguments.AddRange(paths.Select(path => path.Value));

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);

        return backups;
    }

    public async Task DeleteUntrackedAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(paths);

        RequireConfirmation(
            userConfirmed,
            "Takip edilmeyen dosyaları silmek geri alınamaz; bu dosyaların hiçbir kopyası "
            + "git'te yok. İşlem yalnızca kullanıcının açık onayıyla yapılabilir.");

        if (paths.Count == 0)
        {
            // ⚠️ Yolsuz `git clean -f` çalışma ağacının TAMAMINI siler.
            return;
        }

        // `-x`: 🔴 ölçüldü — yok sayılan bir dosyayı `-x` OLMADAN silmeye çalışmak çıkış 0
        // veriyor ve dosya duruyor. Kullanıcı adıyla seçtiği dosyanın silinmesini bekler;
        // "yok sayılıyor olabilir" ayrımı burada anlamsız. Kapsam zaten verilen yollarla
        // sınırlı, tüm depoyu ilgilendirmiyor.
        // `-d`: takip edilmeyen bir dizin seçildiyse onsuz hiçbir şey olmaz.
        List<string> arguments = ["clean", "--force", "-d", "-x", "--quiet", "--"];
        arguments.AddRange(paths.Select(path => path.Value));

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task CleanAsync(
        string workingDirectory,
        CleanOptions options,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        options ??= CleanOptions.Default;

        RequireConfirmation(
            userConfirmed,
            "Çalışma ağacını temizlemek takip edilmeyen tüm dosyaları siler ve geri alınamaz; "
            + "işlem yalnızca kullanıcının açık onayıyla yapılabilir.");

        List<string> arguments = ["clean", "--force"];

        if (options.IncludeNestedRepositories)
        {
            // İkinci `-f`: iç içe depolar için. Tek `-f` onları sessizce atlıyor (ölçüldü).
            arguments.Add("--force");
        }

        if (options.IncludeDirectories)
        {
            arguments.Add("-d");
        }

        if (options.OnlyIgnored)
        {
            arguments.Add("-X");
        }
        else if (options.IncludeIgnored)
        {
            arguments.Add("-x");
        }

        arguments.Add("--quiet");

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitIgnoreOutcome> AddToGitIgnoreAsync(
        string workingDirectory,
        RepositoryPath path,
        string pattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        if (await IsTrackedAsync(workingDirectory, path, cancellationToken).ConfigureAwait(false))
        {
            return GitIgnoreOutcome.PathIsTracked;
        }

        if (await IsIgnoredAsync(workingDirectory, path, cancellationToken).ConfigureAwait(false))
        {
            return GitIgnoreOutcome.AlreadyIgnored;
        }

        string file = Path.Combine(workingDirectory, ".gitignore");
        string existing = File.Exists(file)
            ? await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)
            : string.Empty;

        StringBuilder builder = new(existing);

        // 🔴 ÖLÇÜLDÜ: dosya satır sonuyla bitmiyorsa yeni desen bir öncekine YAPIŞIYOR
        // (`derleme/` + `/kok.txt` → `derleme//kok.txt`). Sonuç, yeni desenin çalışmaması
        // DEĞİL sadece: kullanıcının var olan deseni de bozuluyor.
        if (builder.Length > 0 && builder[^1] is not ('\n' or '\r'))
        {
            builder.Append('\n');
        }

        builder.Append(pattern).Append('\n');

        await File.WriteAllTextAsync(file, builder.ToString(), cancellationToken).ConfigureAwait(false);

        return GitIgnoreOutcome.Added;
    }

    /// <summary>
    /// Atılacak içeriği nesne veritabanına yazar.
    /// </summary>
    /// <remarks>
    /// Diskte olmayan yollar (silinmiş dosyalar) atlanır: <c>hash-object</c> onlarda düşer ve
    /// zaten geri alınacak bir içerikleri yoktur.
    /// </remarks>
    private async Task<IReadOnlyList<DiscardBackup>> BackupAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken)
    {
        List<RepositoryPath> existing =
        [
            .. paths.Where(path => File.Exists(Path.Combine(workingDirectory, path.Value))),
        ];

        List<DiscardBackup> backups = new(existing.Count);

        for (int offset = 0; offset < existing.Count; offset += BackupBatchSize)
        {
            List<RepositoryPath> batch =
                [.. existing.Skip(offset).Take(BackupBatchSize)];

            List<string> arguments = ["hash-object", "-w", "--"];
            arguments.AddRange(batch.Select(path => path.Value));

            GitResult result = await _runner.RunCheckedAsync(
                new GitCommand
                {
                    WorkingDirectory = workingDirectory,
                    Arguments = arguments,

                    // Nesne yazıyor ama index'e dokunmuyor; kuyruğa girmesi gerekmiyor.
                    IsReadOnly = false,
                },
                cancellationToken).ConfigureAwait(false);

            string[] hashes = result.GetStandardOutputText()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (hashes.Length != batch.Count)
            {
                // Hiza bozulduysa yanlış içeriği "yedek" diye sunmaktansa hiç sunmamak yeğdir.
                throw new GitException(
                    GitFailureKind.Unknown,
                    "Yedeklenen içerik sayısı yol sayısıyla uyuşmadı.",
                    "git hash-object -w",
                    result.ExitCode,
                    result.StandardError);
            }

            backups.AddRange(batch.Select((path, index) => new DiscardBackup
            {
                Path = path,
                BlobId = hashes[index].Trim(),
            }));
        }

        return backups;
    }

    private async Task<bool> IsTrackedAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["ls-files", "-z", "--", path.Value],
            },
            cancellationToken).ConfigureAwait(false);

        return result.StandardOutput.Length > 0;
    }

    private async Task<bool> IsIgnoredAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["check-ignore", "--quiet", "--", path.Value],

                // `check-ignore` eşleşme yoksa 1 döner; bu bir hata değil, cevaptır.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    private static void RequireConfirmation(bool userConfirmed, string message)
    {
        if (!userConfirmed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
