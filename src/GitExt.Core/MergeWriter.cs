using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Birleştirme biçimi (P06-T11).</summary>
/// <remarks>
/// Sıra GitExtensions <c>FormMergeBranch</c>'ten (§ 9): <i>Keep single branch (fast forward)</i>
/// · <i>Always create a new merge commit</i> · <i>Squash commits</i>.
/// </remarks>
public enum MergeStrategy
{
    /// <summary>Mümkünse ileri sar, değilse birleştirme commit'i (git'in varsayılanı).</summary>
    Default,

    /// <summary><c>--no-ff</c>: her zaman birleştirme commit'i.</summary>
    NoFastForward,

    /// <summary>
    /// <c>--squash</c>: değişiklikler tek bir değişiklik gibi hazırlanır.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — commit YAPMIYOR.</b> git <i>"Squash commit -- not updating HEAD"</i>
    /// yazıp çıkış kodu <b>0</b> veriyor; <c>HEAD</c> yerinde kalıyor ve değişiklikler
    /// index'te bekliyor. "Başarılı" deyip bırakmak, kullanıcının birleştirdiğini sanıp
    /// commit'lememesi demekti. <see cref="MergeResult.RequiresCommit"/> bunun için var.
    /// </remarks>
    Squash,

    /// <summary><c>--ff-only</c>: ileri sarılamıyorsa hiç yapma.</summary>
    FastForwardOnly,
}

/// <summary>Birleştirmenin nasıl sonuçlandığı (P06-T11).</summary>
public enum MergeOutcome
{
    /// <summary>Yapacak bir şey yoktu.</summary>
    AlreadyUpToDate,

    /// <summary>İleri sarıldı; yeni commit oluşmadı.</summary>
    FastForward,

    /// <summary>Birleştirme commit'i oluştu.</summary>
    MergeCommit,

    /// <summary>Değişiklikler hazırlandı ama <b>commit'lenmedi</b>.</summary>
    Staged,

    /// <summary>Çakışmayla durdu.</summary>
    Conflicted,
}

/// <summary>Birleştirme seçenekleri (P06-T11).</summary>
public sealed record MergeOptions
{
    /// <summary>Birleştirilecek dal ya da commit.</summary>
    public required string Source { get; init; }

    public MergeStrategy Strategy { get; init; }

    /// <summary>Özel birleştirme mesajı; <see langword="null"/> ise git'in varsayılanı.</summary>
    /// <remarks>
    /// Mesaj argüman olarak değil <b>stdin</b> ile geçirilebilirdi, ama <c>git merge</c>
    /// mesajı yalnızca <c>-m</c> ile alıyor. Satır sonu içeren mesajlar için <c>-m</c>
    /// birden çok kez veriliyor (git bunları paragraf olarak birleştiriyor).
    /// </remarks>
    public string? Message { get; init; }

    /// <summary><c>--no-commit</c>: birleştir ama commit'leme.</summary>
    public bool NoCommit { get; init; }

    /// <summary><c>--allow-unrelated-histories</c>.</summary>
    public bool AllowUnrelatedHistories { get; init; }
}

/// <summary>Birleştirme sonucu (P06-T11).</summary>
public sealed record MergeResult
{
    public required MergeOutcome Outcome { get; init; }

    /// <summary>Birleştirme öncesi <c>HEAD</c>.</summary>
    public required string HeadBefore { get; init; }

    /// <summary>Birleştirme sonrası <c>HEAD</c>.</summary>
    public required string HeadAfter { get; init; }

    /// <summary>Çözülmemiş dosyalar.</summary>
    public IReadOnlyList<string> ConflictedPaths { get; init; } = [];

    public bool HasConflicts => ConflictedPaths.Count > 0;

    /// <summary>
    /// Kullanıcının hâlâ commit'lemesi gerekiyor mu?
    /// </summary>
    /// <remarks>
    /// 🔴 <c>--squash</c> ve <c>--no-commit</c> "başarılı" dönüyor ama <c>HEAD</c>
    /// ilerlemiyor. Bunu söylemeyen bir ekran, kullanıcıyı yarım bir işle bırakırdı.
    /// </remarks>
    public bool RequiresCommit => Outcome == MergeOutcome.Staged;

    /// <summary>git'in hazırladığı commit mesajı taslağı (<c>SQUASH_MSG</c>/<c>MERGE_MSG</c>).</summary>
    public string? SuggestedMessage { get; init; }

    /// <summary>
    /// Yapılanı geri alan komut; <c>HEAD</c> ilerlemediyse <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <c>ORIG_HEAD</c> değil <b>hash</b> yazılıyor: sonraki bir merge/reset onu ezer ve
    /// kullanıcı komutu daha sonra çalıştırırsa bambaşka bir yere dönerdi (P06-T07'nin dersi).
    /// </remarks>
    public string? RecoveryCommand => string.Equals(HeadBefore, HeadAfter, StringComparison.Ordinal)
        ? null
        : $"git reset --hard {HeadBefore}";
}

/// <summary>Birleştirmenin ne yapacağının önizlemesi (P06-T11).</summary>
public sealed record MergePreview
{
    /// <summary>Yapacak bir şey var mı?</summary>
    public required bool HasChanges { get; init; }

    /// <summary>İleri sarılabilir mi (ortak ata = <c>HEAD</c>)?</summary>
    public required bool CanFastForward { get; init; }

    /// <summary>Ortak ata var mı? Yoksa geçmişler ilgisiz.</summary>
    public required bool HasCommonAncestor { get; init; }

    /// <summary>Kaynağın <c>HEAD</c>'e göre önündeki commit sayısı.</summary>
    public int Ahead { get; init; }
}

/// <summary>Birleştirme işlemleri (P06-T11, P06-T12).</summary>
public interface IMergeWriter
{
    /// <summary>Birleştirir ve <b>ne olduğunu</b> döndürür.</summary>
    Task<MergeResult> MergeAsync(
        string workingDirectory,
        MergeOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Birleştirme öncesi durumu okur — ekranı doldurmak için.</summary>
    Task<MergePreview> PreviewAsync(
        string workingDirectory,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Süren birleştirmeyi iptal eder (<c>git merge --abort</c>, P06-T12).
    /// </summary>
    Task<string> AbortAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Çalıştırılacak komutu üretir ("komutu göster" ilkesi).</summary>
    string DescribeCommand(MergeOptions options);
}

/// <summary>
/// <c>git merge</c> sarmalayıcısı (P06-T11, P06-T12).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ — çakışma metni <c>stdout</c>'ta.</b> <c>CONFLICT (content): …</c> ve
/// <c>Automatic merge failed…</c> satırları stdout'a yazılıyor, stderr <b>boş</b>. Hata
/// sınıflandırıcı yalnızca stderr'e baktığı için <c>Unknown</c> diyor — P06-T07'de pull'da
/// aynı tuzağa düşülmüştü.
/// </para>
/// <para>
/// → Çakışma kararı <b>metne değil duruma</b> bakıyor: <c>diff --diff-filter=U</c>. Aynı
/// gerekçe sonucun tamamı için geçerli; ne olduğu <c>HEAD</c>'in önce/sonrası ve index'in
/// durumundan hesaplanıyor.
/// </para>
/// </remarks>
public sealed class MergeWriter : IMergeWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public MergeWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<MergeResult> MergeAsync(
        string workingDirectory,
        MergeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Source);

        string before = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        try
        {
            await _writer
                .RunAsync(workingDirectory, BuildArguments(options), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException)
        {
            // Çakışma bir hata değil, bir DURUM. Ama gerçek hatalar (kirli ağaç, bilinmeyen
            // ref, ilgisiz geçmişler) olduğu gibi yukarı gitmeli — ayrım index'e bakarak
            // yapılıyor, git'in metnine değil.
            if (await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false)
                is not { Count: > 0 } conflicts)
            {
                throw;
            }

            return new MergeResult
            {
                Outcome = MergeOutcome.Conflicted,
                HeadBefore = before,
                HeadAfter = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false),
                ConflictedPaths = conflicts,
                SuggestedMessage = await ReadDraftAsync(workingDirectory, "MERGE_MSG", cancellationToken)
                    .ConfigureAwait(false),
            };
        }

        string after = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        bool moved = !string.Equals(before, after, StringComparison.Ordinal);

        // 🔴 `--squash` ve `--no-commit` çıkış kodu 0 veriyor ama HEAD yerinde. Hazırlanan
        // değişiklik var mı diye index'e bakılıyor; yoksa gerçekten yapacak bir şey yoktu.
        bool staged = !moved
            && await HasStagedChangesAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        MergeOutcome outcome = staged
            ? MergeOutcome.Staged
            : !moved
                ? MergeOutcome.AlreadyUpToDate
                : await IsMergeCommitAsync(workingDirectory, after, cancellationToken).ConfigureAwait(false)
                    ? MergeOutcome.MergeCommit
                    : MergeOutcome.FastForward;

        return new MergeResult
        {
            Outcome = outcome,
            HeadBefore = before,
            HeadAfter = after,
            SuggestedMessage = staged
                ? await ReadDraftAsync(
                        workingDirectory,
                        options.Strategy == MergeStrategy.Squash ? "SQUASH_MSG" : "MERGE_MSG",
                        cancellationToken)
                    .ConfigureAwait(false)
                : null,
        };
    }

    public async Task<MergePreview> PreviewAsync(
        string workingDirectory,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        GitResult ancestor = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "merge-base", "HEAD", source),
            cancellationToken).ConfigureAwait(false);

        if (!ancestor.IsSuccess)
        {
            return new MergePreview { HasChanges = true, CanFastForward = false, HasCommonAncestor = false };
        }

        string mergeBase = ancestor.GetStandardOutputText().Trim();
        string head = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        string counts = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--count", $"HEAD..{source}"),
            cancellationToken).ConfigureAwait(false);

        int ahead = int.TryParse(counts.Trim(), out int parsed) ? parsed : 0;

        return new MergePreview
        {
            HasChanges = ahead > 0,
            CanFastForward = string.Equals(mergeBase, head, StringComparison.Ordinal),
            HasCommonAncestor = true,
            Ahead = ahead,
        };
    }

    public async Task<string> AbortAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        await _writer
            .RunAsync(workingDirectory, ["merge", "--abort"], cancellationToken)
            .ConfigureAwait(false);

        return await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    public string DescribeCommand(MergeOptions options) => Describe(options);

    /// <summary>Çalıştırılacak komutu üretir ("komutu göster" ilkesi).</summary>
    public static string Describe(MergeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return "git " + string.Join(' ', BuildArguments(options));
    }

    /// <remarks>
    /// <c>--</c> ayracı her zaman: <c>-</c> ile başlayan bir dal adı aksi hâlde bayrak
    /// sanılırdı (P06-T01'in dersi).
    /// </remarks>
    private static IReadOnlyList<string> BuildArguments(MergeOptions options)
    {
        List<string> arguments = ["merge"];

        switch (options.Strategy)
        {
            case MergeStrategy.NoFastForward:
                arguments.Add("--no-ff");
                break;
            case MergeStrategy.Squash:
                arguments.Add("--squash");
                break;
            case MergeStrategy.FastForwardOnly:
                arguments.Add("--ff-only");
                break;
            case MergeStrategy.Default:
            default:
                break;
        }

        if (options.NoCommit && options.Strategy != MergeStrategy.Squash)
        {
            // `--squash` zaten commit'lemiyor; ikisini birlikte vermek gereksiz.
            arguments.Add("--no-commit");
        }

        if (options.AllowUnrelatedHistories)
        {
            arguments.Add("--allow-unrelated-histories");
        }

        if (options.Message is { Length: > 0 } message)
        {
            foreach (string paragraph in message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                arguments.Add("-m");
                arguments.Add(paragraph.TrimEnd('\r'));
            }
        }

        arguments.Add("--");
        arguments.Add(options.Source);

        return arguments;
    }

    private async Task<string> ReadHeadAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "HEAD"),
            cancellationToken).ConfigureAwait(false);

        // Doğmamış depoda `rev-parse HEAD` başarısız; birleştirilecek bir şey de yok.
        return result.IsSuccess ? result.GetStandardOutputText().Trim() : string.Empty;
    }

    private async Task<IReadOnlyList<string>> ReadConflictsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? [.. result.GetStandardOutputText().Split('\0', StringSplitOptions.RemoveEmptyEntries)]
            : [];
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

    private async Task<bool> IsMergeCommitAsync(
        string workingDirectory,
        string commit,
        CancellationToken cancellationToken)
    {
        string parents = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-list", "--parents", "-1", commit),
            cancellationToken).ConfigureAwait(false);

        // "<commit> <ebeveyn1> <ebeveyn2>" — iki ebeveyn varsa birleştirme commit'i.
        return parents.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 2;
    }

    /// <summary>git'in bıraktığı mesaj taslağını okur.</summary>
    private async Task<string?> ReadDraftAsync(
        string workingDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        string gitDirectory = await _runner.RunForTextAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "--absolute-git-dir"),
            cancellationToken).ConfigureAwait(false);

        string path = Path.Combine(gitDirectory.Trim(), fileName);

        if (!File.Exists(path))
        {
            return null;
        }

        string text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        // Yorum satırları kullanıcıya gösterilmiyor; git zaten commit'te atıyor.
        return string.Join(
            '\n',
            text.Split('\n').Where(line => !line.StartsWith('#'))).Trim();
    }
}
