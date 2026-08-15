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
/// onu <b>anında</b> siliyor (ölçüldü). Buna karşılık <b>düz <c>git gc</c> silmiyor</b>
/// (P05-T15'te ölçüldü): dangling nesneler varsayılan <c>gc.pruneExpire=2.weeks</c> boyunca
/// korunuyor. Yani yedek gerçek bir kurtarma yolu, ama süresiz değil — yıkıcı işlem yine de
/// <b>açık onay</b> istiyor.
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
    /// <returns>
    /// Silinen içeriğin yedekleri.
    /// </returns>
    /// <remarks>
    /// <b>⚠️ ÖLÇÜLDÜ (P05-T15):</b> <c>git clean</c> ile silinen bir dosyanın nesne
    /// veritabanında <b>hiçbir izi kalmıyor</b> — <c>git fsck --lost-found</c> bile
    /// bulmuyor. Bu yüzden içerik silinmeden önce <c>hash-object -w</c> ile yedekleniyor;
    /// takip edilmeyen dosyalar tipik olarak <b>henüz commit edilmemiş yeni kaynak
    /// dosyalardır</b> ve kaybı telafi edilemez.
    /// </remarks>
    Task<IReadOnlyList<DiscardBackup>> DeleteUntrackedAsync(
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
    /// Bir dosyanın <b>seçili satırlarındaki</b> değişiklikleri geri alır (P05-T15).
    /// </summary>
    /// <returns>Atılan içeriğin yedekleri.</returns>
    /// <remarks>
    /// <para>
    /// <b>ÖLÇÜLDÜ:</b> <c>git apply --reverse</c> (yani <c>--cached</c> OLMADAN) yamayı
    /// yalnızca <b>çalışma ağacına</b> uyguluyor; index'e dokunmuyor. Dosyanın
    /// stage'lenmiş bir sürümü varsa o olduğu gibi kalıyor — git'in kendi davranışı bu ve
    /// "şu satırları geri al" komutundan beklenen de bu.
    /// </para>
    /// <para>
    /// Kısmi geri alma da yıkıcı: dosyanın <b>tamamı</b> önceden yedekleniyor, çünkü geri
    /// alma işlemi dosyayı yamadan önceki hâline döndürmek zorunda.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<DiscardBackup>> DiscardPartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        bool userConfirmed,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir yedeği çalışma ağacına geri yazar (P05-T15).
    /// </summary>
    /// <returns>Gerçekten geri yazılan yedekler.</returns>
    /// <remarks>
    /// <para>
    /// Yedek almak tek başına güvenlik ağı değil: kullanıcıya blob kimliği verip
    /// <c>git cat-file</c> yazmasını beklemek, panik anında işe yaramaz. Geri yazma
    /// <b>uygulamanın sunduğu bir işlem</b> olmalı.
    /// </para>
    /// <para>
    /// İçerik <b>ham baytlarla</b> yazılıyor. Yedek <c>--no-filters</c> ile alındığı için
    /// blob dosyanın diskteki hâlinin birebir kopyası; yazarken de dönüştürülmemeli.
    /// </para>
    /// <para>
    /// Nesne artık yoksa (<c>gc --prune=now</c>) o yedek <b>sessizce atlanır</b>: kısmi
    /// kurtarma, hiç kurtarmamaktan iyidir.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Verilen yolların diskteki hâlini nesne veritabanına yedekler (P06-T02).
    /// </summary>
    /// <returns>Diskte bulunan yolların yedekleri; olmayanlar atlanır.</returns>
    /// <remarks>
    /// Yıkıcı bir işlemden <b>önce</b> çağrılır. Ayrı bir işlem olarak açıldı çünkü
    /// dal değiştirme de (<c>switch --discard-changes</c>) aynı güvenlik ağına ihtiyaç
    /// duyuyor ve <c>--no-filters</c> tuzağının ikinci kez yazılması, ikinci kez
    /// unutulabilmesi demekti.
    /// </remarks>
    Task<IReadOnlyList<DiscardBackup>> BackupPathsAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscardBackup>> RestoreBackupsAsync(
        string workingDirectory,
        IReadOnlyList<DiscardBackup> backups,
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
            "Reverting the changes deletes uncommitted content, and there is no way back through "
            + "the reflog; the operation can only be performed with the user's explicit consent.");

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

    public async Task<IReadOnlyList<DiscardBackup>> DeleteUntrackedAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(paths);

        RequireConfirmation(
            userConfirmed,
            "Deleting untracked files cannot be undone; there is no copy of these files "
            + "does not exist in git. The operation can only be performed with the user's explicit consent.");

        if (paths.Count == 0)
        {
            // ⚠️ Yolsuz `git clean -f` çalışma ağacının TAMAMINI siler.
            return [];
        }

        // 🔴 P05-T15'te eklendi. ÖLÇÜLDÜ ve tasarımı değiştirdi: `git clean` ile silinen
        // takip edilmeyen bir dosyanın nesne veritabanında **hiçbir izi yok**
        // (`fsck --lost-found` bile bulmuyor) — yani bu, deponun tek gerçekten
        // geri döndürülemez işlemiydi. Oysa takip edilmeyen dosyalar tipik olarak
        // **henüz commit edilmemiş yeni kaynak dosyalar**: bu deponun kendisinde
        // `git clean -dn` çıktısı o sırada yazılmakta olan dosyaları listeliyordu.
        // Yedeklemek ucuz (500 dosya = 110 ms), kaybı telafi edilemez.
        IReadOnlyList<DiscardBackup> backups =
            await BackupAsync(workingDirectory, paths, cancellationToken).ConfigureAwait(false);

        // `-x`: 🔴 ölçüldü — yok sayılan bir dosyayı `-x` OLMADAN silmeye çalışmak çıkış 0
        // veriyor ve dosya duruyor. Kullanıcı adıyla seçtiği dosyanın silinmesini bekler;
        // "yok sayılıyor olabilir" ayrımı burada anlamsız. Kapsam zaten verilen yollarla
        // sınırlı, tüm depoyu ilgilendirmiyor.
        // `-d`: takip edilmeyen bir dizin seçildiyse onsuz hiçbir şey olmaz.
        List<string> arguments = ["clean", "--force", "-d", "-x", "--quiet", "--"];
        arguments.AddRange(paths.Select(path => path.Value));

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);

        return backups;
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
            "Cleaning the working tree deletes every untracked file and cannot be undone; "
            + "the operation can only be performed with the user's explicit consent.");

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

    public async Task<IReadOnlyList<DiscardBackup>> DiscardPartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        bool userConfirmed,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(selection);

        RequireConfirmation(
            userConfirmed,
            "Reverting the changes on the selected lines deletes content in the working tree. "
            + "The operation can only be performed with the user's explicit consent.");

        // Ters uygulanacağı için yama "stage" yönünde üretiliyor: yamayı üretmek ile
        // uygulamak ayrı kararlar (P05-T04).
        string? patch = PatchBuilder.Build(diff, selection, PatchDirection.Stage);

        if (patch is null)
        {
            // Seçilen bir şey yok.
            return [];
        }

        IReadOnlyList<DiscardBackup> backups =
            await BackupAsync(workingDirectory, [diff.Path], cancellationToken).ConfigureAwait(false);

        // ⚠️ `--cached` YOK: yama yalnızca çalışma ağacına uygulanmalı (ölçüldü).
        await _writer
            .RunAsync(
                workingDirectory,
                ["apply", "--reverse", "-"],
                patch,
                contentEncoding,
                cancellationToken)
            .ConfigureAwait(false);

        return backups;
    }

    public async Task<IReadOnlyList<DiscardBackup>> RestoreBackupsAsync(
        string workingDirectory,
        IReadOnlyList<DiscardBackup> backups,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(backups);

        if (backups.Count == 0)
        {
            return [];
        }

        // 🔴 ÖLÇÜLDÜ: yedek başına ayrı `cat-file -p` süreci 200 dosyada **671 ms**,
        // `--batch` ile tek süreçte **9 ms** (75×). Kurtarma kullanıcının beklediği bir
        // işlem; büyük bir sıfırlamanın geri alınması saniyeler sürmemeli.
        StringBuilder request = new();

        foreach (DiscardBackup backup in backups)
        {
            request.Append(backup.BlobId).Append('\n');
        }

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["cat-file", "--batch"],
                IsReadOnly = true,
                StandardInput = System.Text.Encoding.ASCII.GetBytes(request.ToString()),
            },
            cancellationToken).ConfigureAwait(false);

        List<DiscardBackup> restored = new(backups.Count);
        int offset = 0;

        foreach (DiscardBackup backup in backups)
        {
            if (!TryReadBatchEntry(result.StandardOutput, ref offset, out ReadOnlyMemory<byte> content))
            {
                // Nesne budanmış (`gc --prune=now`) → git `<oid> missing` yazıyor.
                // Kurtarılamayan bir yedek hata değil; diğerlerine devam edilir.
                continue;
            }

            string target = Path.Combine(workingDirectory, backup.Path.Value);

            // Silinen dosyanın dizini de silinmiş olabilir (`clean -d`).
            string? directory = Path.GetDirectoryName(target);

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(target, content, cancellationToken).ConfigureAwait(false);

            restored.Add(backup);
        }

        return restored;
    }

    /// <summary>
    /// <c>cat-file --batch</c> akışından sıradaki nesnenin içeriğini okur.
    /// </summary>
    /// <remarks>
    /// Biçim: <c>&lt;oid&gt; &lt;tür&gt; &lt;boyut&gt;\n&lt;içerik&gt;\n</c>, bulunamayan
    /// nesne için <c>&lt;oid&gt; missing\n</c>. İçerik <b>bayt olarak</b> alınıyor: boyut
    /// başlıkta yazdığı için ikili veride ayraç aramak gerekmiyor — yedeğin birebir
    /// olması bu görevin tüm amacı (P05-T15).
    /// </remarks>
    /// <returns>Nesne okunduysa <see langword="true"/>; eksikse <see langword="false"/>.</returns>
    private static bool TryReadBatchEntry(
        byte[] stream,
        ref int offset,
        out ReadOnlyMemory<byte> content)
    {
        content = default;

        int lineEnd = Array.IndexOf(stream, (byte)'\n', offset);

        if (lineEnd < 0)
        {
            return false;
        }

        string header = System.Text.Encoding.ASCII.GetString(stream, offset, lineEnd - offset);
        offset = lineEnd + 1;

        string[] parts = header.Split(' ');

        // `<oid> missing` — üç alan yoksa içerik de yok.
        if (parts.Length < 3 || !int.TryParse(parts[2], out int size))
        {
            return false;
        }

        if (offset + size > stream.Length)
        {
            return false;
        }

        content = stream.AsMemory(offset, size);

        // İçerikten sonra git bir satır sonu daha yazıyor.
        offset += size + 1;

        return true;
    }

    /// <summary>
    /// Atılacak içeriği nesne veritabanına yazar.
    /// </summary>
    /// <remarks>
    /// Diskte olmayan yollar (silinmiş dosyalar) atlanır: <c>hash-object</c> onlarda düşer ve
    /// zaten geri alınacak bir içerikleri yoktur.
    /// </remarks>
    public Task<IReadOnlyList<DiscardBackup>> BackupPathsAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default) =>
        BackupAsync(workingDirectory, paths, cancellationToken);

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

            // 🔴 `--no-filters` ŞART (P05-T15'te ölçüldü). Onsuz git, yedeği yazarken
            // "clean" filtrelerini uyguluyor ve yedek **birebir olmuyor**:
            //   · `.gitattributes`'ta `text=auto` varsa CRLF → LF (geri yazımda satır
            //     sonları sessizce değişir),
            //   · özel bir clean filtresi (Git LFS'in çalışma biçimi) varsa yedeğe
            //     **dosyanın kendisi değil filtrenin çıktısı** girer — ölçümde
            //     `GIZLI parola` içeriği yedekte `*** parola` oldu.
            // Kurtarma vaadi veren bir yedeğin içeriği değiştirmesi, hiç yedek almamaktan
            // daha kötüdür: kullanıcı kurtardığını sanır.
            List<string> arguments = ["hash-object", "-w", "--no-filters", "--"];
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
                    "The number of backed-up contents did not match the number of paths.",
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
