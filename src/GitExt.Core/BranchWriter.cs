using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Dal oluşturma seçenekleri (P06-T01).
/// </summary>
public sealed record BranchCreateOptions
{
    /// <summary>Oluşturulacak dalın adı.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Başlangıç noktası: commit hash'i, dal veya etiket adı. <see langword="null"/> ise
    /// <c>HEAD</c>.
    /// </summary>
    public string? StartPoint { get; init; }

    /// <summary>
    /// Oluşturduktan sonra dala geçilsin mi? Varsayılan <see langword="true"/> —
    /// GitExtensions'ta <c>chkCheckoutAfterCreate</c> da işaretli geliyor (§ 9).
    /// </summary>
    public bool Checkout { get; init; } = true;
}

/// <summary>
/// Dal oluşturmanın sonucu (P06-T01).
/// </summary>
/// <param name="Name">Oluşturulan dalın adı.</param>
/// <param name="CheckedOut">Dala geçildi mi?</param>
/// <param name="Upstream">
/// git'in <b>kendiliğinden</b> kurduğu upstream, kurulmadıysa <see langword="null"/>.
/// </param>
public sealed record BranchCreateResult(string Name, bool CheckedOut, string? Upstream);


/// <summary>
/// Dal değiştirirken yerel değişikliklere ne yapılacağı (P06-T02).
/// </summary>
/// <remarks>
/// Sıra GitExtensions'ın <c>FormCheckoutBranch</c> "Local changes" grubundan (§ 9):
/// <i>Don't change · Merge · Stash · Reset</i>.
/// </remarks>
public enum LocalChangesAction
{
    /// <summary>Dokunma. git değişiklikleri taşıyabilirse taşır, taşıyamazsa reddeder.</summary>
    Keep,

    /// <summary>
    /// <c>--merge</c>: değişiklikleri hedefe birleştirmeyi dene.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ:</b> bu yol çakışmada <b>çıkış kodu 0</b> veriyor ve ağacı
    /// <b>birleşmemiş</b> bırakıyor (üstelik gizli bir autostash bırakarak). Çıkış koduna
    /// bakan bir arayüz "başarıyla geçildi" derdi.
    /// </remarks>
    Merge,

    /// <summary>
    /// <c>git stash push -u</c> ile kenara al.
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ — en yetenekli seçenek bu.</b> <c>-u</c> ile takip edilmeyen dosyalar da
    /// alındığı için <i>"takip edilmeyen dosya üzerine yazılacaktı"</i> çakışmasını da
    /// çözüyor; <see cref="Discard"/> ise o durumda <b>reddediyor</b>. Üstelik yıkıcı değil.
    /// </remarks>
    Stash,

    /// <summary>
    /// <c>--discard-changes</c>: yerel değişiklikleri at.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>YIKICI ve ÖLÇÜMLE DOĞRULANDI:</b> stage'lenmiş içerik nesne veritabanında
    /// dangling blob olarak kalıyor ama <b>stage'lenmemiş içeriğin hiçbir izi yok</b> —
    /// tam olarak P05-T15'teki <c>git clean</c> durumu. Bu yüzden önce yedek alınır.
    /// </remarks>
    Discard,
}

/// <summary>
/// Dal değiştirme seçenekleri (P06-T02).
/// </summary>
public sealed record BranchSwitchOptions
{
    /// <summary>Hedef: dal adı veya commit.</summary>
    public required string Target { get; init; }

    /// <summary>Dal yerine doğrudan commit'e geç (detached HEAD).</summary>
    public bool Detach { get; init; }

    /// <summary>Yerel değişikliklere ne yapılacak?</summary>
    public LocalChangesAction LocalChanges { get; init; } = LocalChangesAction.Keep;

    /// <summary>
    /// <see cref="LocalChangesAction.Discard"/> için <b>zorunlu</b> açık onay.
    /// </summary>
    public bool UserConfirmed { get; init; }
}

/// <summary>
/// Dal değiştirmenin sonucu (P06-T02).
/// </summary>
public sealed record BranchSwitchResult
{
    public required string Target { get; init; }

    /// <summary>
    /// Ağaç <b>birleşmemiş</b> dosyalarla mı kaldı?
    /// </summary>
    /// <remarks>
    /// Çıkış kodu bunu söylemiyor (ölçüldü); durum ayrıca okunuyor.
    /// </remarks>
    public bool HasConflicts { get; init; }

    /// <summary>Yerel değişiklikler bir stash'e alındı mı?</summary>
    public bool StashCreated { get; init; }

    /// <summary>Atılan içeriğin yedekleri (yalnızca <see cref="LocalChangesAction.Discard"/>).</summary>
    public IReadOnlyList<DiscardBackup> Backups { get; init; } = [];
}


/// <summary>
/// Dal silmenin sonucu (P06-T03).
/// </summary>
public sealed record BranchDeleteResult
{
    public required string Name { get; init; }

    /// <summary>
    /// Silinen dalın son işaret ettiği commit.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Kurtarmanın tek güvenilir yolu bu.</b> ÖLÇÜLDÜ: dal silinince <b>kendi
    /// reflog'u da siliniyor</b>, ve HEAD reflog'unda iz olması yalnızca o dalda
    /// <b>bu çalışma ağacında</b> çalışılmışsa geçerli. Bağlı bir worktree'de üretilmiş
    /// dal silindiğinde <b>hiçbir reflog izi kalmıyor</b> — commit'e yalnızca
    /// <c>fsck --unreachable</c> ile ulaşılıyor. Bu yüzden hash silmeden <b>önce</b>
    /// okunuyor ve kullanıcıya veriliyor.
    /// </remarks>
    public required string LastCommitId { get; init; }

    /// <summary>Dal <c>--force</c> gerektirdi mi (yani merge edilmemişti)?</summary>
    public bool WasUnmerged { get; init; }
}

/// <summary>
/// Dal silme reddedildiğinde, nedeni ayırt etmek için (P06-T03).
/// </summary>
public sealed class BranchNotMergedException : Exception
{
    public BranchNotMergedException(string name, string lastCommitId)
        : base($"Branch '{name}' contains commits that are not merged anywhere.")
    {
        Name = name;
        LastCommitId = lastCommitId;
    }

    public string Name { get; }

    /// <summary>Dalın ucu — kullanıcıya kurtarma yolu olarak gösterilir.</summary>
    public string LastCommitId { get; }
}

/// <summary>
/// Dal yazma işlemleri (P06-T01).
/// </summary>
public interface IBranchWriter
{
    /// <summary>
    /// Yeni bir dal oluşturur, istenirse ona geçer.
    /// </summary>
    /// <exception cref="ArgumentException">Ad geçersiz.</exception>
    /// <exception cref="GitException">
    /// Dal zaten var, ad çakışıyor, başlangıç noktası çözümlenemedi ya da çalışma ağacı kirli.
    /// </exception>
    Task<BranchCreateResult> CreateAsync(
        string workingDirectory,
        BranchCreateOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Başka bir dala veya commit'e geçer (P06-T02).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="LocalChangesAction.Discard"/> seçildi ama onay verilmedi.
    /// </exception>
    /// <exception cref="GitException">Hedef çözümlenemedi ya da geçiş reddedildi.</exception>
    Task<BranchSwitchResult> SwitchAsync(
        string workingDirectory,
        BranchSwitchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dalı yeniden adlandırır (P06-T03).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Zorla (<c>-M</c>) yeniden adlandırma SUNULMUYOR.</b> ÖLÇÜLDÜ: var olan bir ada
    /// <c>-M</c> ile yeniden adlandırmak o dalı <b>sessizce eziyor</b> — ölçümde hedef dal
    /// hiçbir uyarı olmadan yok oldu. Ad çakışması hata olarak bildirilir.
    /// </remarks>
    /// <exception cref="ArgumentException">Yeni ad geçersiz.</exception>
    /// <exception cref="GitException">Ad zaten var ya da dal bulunamadı.</exception>
    Task RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dalı siler (P06-T03).
    /// </summary>
    /// <param name="workingDirectory">Depo çalışma dizini.</param>
    /// <param name="name">Silinecek dalın adı.</param>
    /// <param name="cancellationToken">İptal belirteci.</param>
    /// <param name="force">
    /// Birleştirilmemiş dal da silinsin mi? <see langword="false"/> iken git reddederse
    /// <see cref="BranchNotMergedException"/> fırlatılır.
    /// </param>
    /// <exception cref="BranchNotMergedException">Dal birleştirilmemiş ve zorlama yok.</exception>
    /// <exception cref="GitException">Dal bir çalışma ağacında checkout edilmiş.</exception>
    Task<BranchDeleteResult> DeleteAsync(
        string workingDirectory,
        string name,
        bool force = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git branch</c> / <c>git switch -c</c> sarmalayıcısı (P06-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ — neden iki ayrı komut?</b> Fark yalnızca kolaylık değil, <b>güvenlik</b>:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>git branch</c> çalışma ağacına <b>hiç dokunmuyor</b>; kirli ağaçta bile her zaman
///     başarılı oluyor.
///   </description></item>
///   <item><description>
///     <c>git switch -c</c> ise checkout yapıyor ve kirli ağaçta <b>reddedebiliyor</b>
///     (çıkış kodu <b>1</b>, ad hatalarının <b>128</b>'inden farklı). Reddettiğinde
///     <b>dalı da oluşturmuyor</b> — ölçümde doğrulandı: ne dal kaldı ne de dal değişti.
///     Yani kısmi bir sonuç yok, kullanıcı bir şey kaybetmiyor.
///   </description></item>
/// </list>
/// <para>
/// <b>ÖLÇÜLDÜ — upstream kendiliğinden kuruluyor.</b> Başlangıç noktası bir uzak izleme dalıysa
/// (<c>origin/x</c>) git upstream'i kendisi ayarlıyor (<c>branch.autoSetupMerge</c>
/// varsayılanı); yerel bir daldan oluşturulduğunda <b>ayarlamıyor</b>. Bunu biz taklit
/// etmiyoruz — sonuçta ne olduğunu <b>okuyup</b> bildiriyoruz, çünkü kullanıcının ayarı
/// bunu değiştirebilir.
/// </para>
/// </remarks>
public sealed class BranchWriter : IBranchWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly IWorkingTreeWriter? _backup;

    /// <param name="writer">Yazma kuyruğuna giren git çağrıları için.</param>
    /// <param name="runner">Salt okunur git çağrıları için.</param>
    /// <param name="backup">
    /// Yıkıcı geçişten önce yedek alan yazıcı. <see langword="null"/> ise
    /// <see cref="LocalChangesAction.Discard"/> <b>reddedilir</b> — güvenlik ağı olmadan
    /// geri getirilemez içerik silinmez (P05-T15 kuralı).
    /// </param>
    public BranchWriter(
        IGitWriter writer,
        IGitProcessRunner runner,
        IWorkingTreeWriter? backup = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
        _backup = backup;
    }

    public async Task<BranchCreateResult> CreateAsync(
        string workingDirectory,
        BranchCreateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Ad doğrulaması git'e bırakılmıyor: git'in cevabı çıkış kodu 128 ve serbest metin,
        // oysa arayüz kullanıcı YAZARKEN "neden geçersiz" diyebilmeli.
        if (BranchName.Validate(options.Name) is { } problem)
        {
            throw new ArgumentException(
                $"'{options.Name}' is not a valid branch name ({problem}).", nameof(options));
        }

        // Doğmamış HEAD'i önce eliyoruz: git'in mesajı ("not a valid object name: 'main'")
        // UnknownRevision'a düşer ve kullanıcıya "dal bulunamadı" der — oysa sorun deponun
        // boş olması (ölçüldü).
        if (options.StartPoint is null
            && !await HasCommitsAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
        {
            throw new GitException(
                GitFailureKind.UnbornHead,
                GitFailureClassifier.Describe(GitFailureKind.UnbornHead),
                "git branch",
                exitCode: 128,
                standardError: string.Empty);
        }

        // ⚠️ `--` ayracı: adın tire ile başlamadığını doğruladık ama başlangıç noktası
        // kullanıcıdan geliyor; ayraç olmadan `-x` bir seçenek sanılırdı.
        IReadOnlyList<string> arguments = options.Checkout
            ? ["switch", "--create", options.Name, .. StartPointArgument(options)]
            : ["branch", "--", options.Name, .. StartPointArgument(options)];

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);

        string? upstream = await ReadUpstreamAsync(workingDirectory, options.Name, cancellationToken)
            .ConfigureAwait(false);

        return new BranchCreateResult(options.Name, options.Checkout, upstream);
    }

    public async Task RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);

        if (BranchName.Validate(newName) is { } problem)
        {
            throw new ArgumentException(
                $"'{newName}' is not a valid branch name ({problem}).", nameof(newName));
        }

        // ⚠️ `-m`, ASLA `-M`: ölçümde `-M` var olan hedef dalı sessizce yok etti.
        // Upstream ve dalın kendi reflog'u `-m` ile korunuyor (ölçüldü).
        await _writer
            .RunAsync(workingDirectory, ["branch", "-m", "--", oldName, newName], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BranchDeleteResult> DeleteAsync(
        string workingDirectory,
        string name,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // 🔴 Hash silmeden ÖNCE okunuyor: silme sonrası dalın kendi reflog'u da gidiyor ve
        // bağlı worktree'de üretilmiş bir dalda HİÇBİR reflog izi kalmıyor (ölçüldü).
        string lastCommit = await RunTextAsync(
                workingDirectory, ["rev-parse", "--verify", "--quiet", BranchName.HeadsPrefix + name],
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _writer
                .RunAsync(
                    workingDirectory,
                    ["branch", force ? "-D" : "-d", "--", name],
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException error) when (!force && IsNotFullyMerged(error))
        {
            // 🔑 Birleşmişliği KENDİMİZ hesaplamıyoruz. ÖLÇÜLDÜ: `-d`, dalı HEAD'e değil
            // **upstream'ine** birleşmiş olsa da siliyor (uyarıyla, çıkış kodu 0).
            // `merge-base --is-ancestor … HEAD` ile karar verseydik bu dallar için
            // yanlış "birleştirilmemiş" alarmı üretirdik. Kararı git veriyor.
            throw new BranchNotMergedException(name, lastCommit);
        }

        return new BranchDeleteResult
        {
            Name = name,
            LastCommitId = lastCommit,
            WasUnmerged = force,
        };
    }

    private static bool IsNotFullyMerged(GitException error) =>
        error.StandardError.Contains("not fully merged", StringComparison.Ordinal);

    private async Task<string> RunTextAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand { WorkingDirectory = workingDirectory, Arguments = arguments },
            cancellationToken).ConfigureAwait(false);

        return result.GetStandardOutputText().Trim();
    }

    public async Task<BranchSwitchResult> SwitchAsync(
        string workingDirectory,
        BranchSwitchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Target);

        IReadOnlyList<DiscardBackup> backups = [];
        bool stashed = false;

        if (options.LocalChanges == LocalChangesAction.Discard)
        {
            if (!options.UserConfirmed)
            {
                throw new InvalidOperationException(
                    "Switching branches while discarding local changes deletes content irreversibly; "
                    + "the operation can only be performed with the user's explicit consent.");
            }

            if (_backup is null)
            {
                throw new InvalidOperationException(
                    "Local changes cannot be discarded without a backup writer.");
            }

            // 🔴 Stage'lenmemiş içeriğin nesne veritabanında HİÇBİR izi kalmıyor (ölçüldü);
            // yedek, bu yolun tek geri dönüş imkânı.
            backups = await _backup
                .BackupPathsAsync(
                    workingDirectory,
                    await DirtyTrackedPathsAsync(workingDirectory, cancellationToken)
                        .ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (options.LocalChanges == LocalChangesAction.Stash)
        {
            // `-u` ŞART: onsuz takip edilmeyen dosyalar çalışma ağacında kalır ve
            // "takip edilmeyen dosya üzerine yazılacaktı" çakışması çözülmez (ölçüldü).
            stashed = await TryStashAsync(workingDirectory, options.Target, cancellationToken)
                .ConfigureAwait(false);
        }

        List<string> arguments = ["switch"];

        if (options.Detach)
        {
            arguments.Add("--detach");
        }

        if (options.LocalChanges == LocalChangesAction.Merge)
        {
            arguments.Add("--merge");
        }
        else if (options.LocalChanges == LocalChangesAction.Discard)
        {
            arguments.Add("--discard-changes");
        }

        arguments.Add("--");
        arguments.Add(options.Target);

        try
        {
            await _writer.RunAsync(workingDirectory, arguments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException)
        {
            // Stash'ledikten sonra geçiş başarısızsa kullanıcı, kendi istemediği bir
            // stash'in içinde kalmış olurdu. Durumu olduğu gibi geri veriyoruz.
            if (stashed)
            {
                await PopStashAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        // 🔴 Çıkış kodu 0 ÇAKIŞMA OLMADIĞI ANLAMINA GELMİYOR (`--merge` ölçümü); durum
        // ayrıca okunuyor.
        bool conflicts = await HasUnmergedPathsAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return new BranchSwitchResult
        {
            Target = options.Target,
            HasConflicts = conflicts,
            StashCreated = stashed,
            Backups = backups,
        };
    }

    /// <summary>Değişmiş <b>takip edilen</b> yollar — yedeklenecek olanlar.</summary>
    /// <remarks>
    /// Takip edilmeyen dosyalar dışarıda: <c>--discard-changes</c> onlara <b>dokunmuyor</b>
    /// (ölçüldü), dolayısıyla yedeklemek gereksiz iş olurdu.
    /// </remarks>
    private async Task<IReadOnlyList<RepositoryPath>> DirtyTrackedPathsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["diff", "--name-only", "-z", "HEAD"],
            },
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. result
                .SplitStandardOutputAtNul()
                .Where(value => value.Length > 0)
                .Select(RepositoryPath.Parse),
        ];
    }

    private async Task<bool> HasUnmergedPathsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // `diff --name-only --diff-filter=U` yalnızca birleşmemiş yolları verir; insan
        // okunur çıktı ayrıştırılmıyor.
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["diff", "--name-only", "--diff-filter=U", "-z"],
            },
            cancellationToken).ConfigureAwait(false);

        return result.SplitStandardOutputAtNul().Any(value => value.Length > 0);
    }

    /// <summary>Stash oluşturur; atılacak bir şey yoksa <see langword="false"/> döner.</summary>
    private async Task<bool> TryStashAsync(
        string workingDirectory,
        string target,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments =
                [
                    "stash", "push", "--include-untracked", "--quiet",
                    "--message", $"gitext: switch to branch '{target}'",
                ],
                IsReadOnly = false,
            },
            cancellationToken).ConfigureAwait(false);

        // Temiz ağaçta `stash push` hata vermiyor ama stash de oluşturmuyor; sonucu
        // stash listesinden okumak, çıktı metnini ayrıştırmaktan güvenli.
        _ = result;

        return await HasStashAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasStashAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", "refs/stash"],
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    private Task PopStashAsync(string workingDirectory, CancellationToken cancellationToken) =>
        _writer.RunAsync(workingDirectory, ["stash", "pop"], cancellationToken);

    private static string[] StartPointArgument(BranchCreateOptions options) =>
        options.StartPoint is { Length: > 0 } start ? [start] : [];

    /// <summary>
    /// git'in upstream'i kendiliğinden kurup kurmadığını <b>okur</b>.
    /// </summary>
    /// <remarks>
    /// Boş dize ile "upstream yok" aynı şey; <c>for-each-ref</c> ikisini de boş döndürür.
    /// </remarks>
    private async Task<string?> ReadUpstreamAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments =
                [
                    "for-each-ref",
                    "--format=%(upstream:short)",
                    BranchName.HeadsPrefix + name,
                ],
            },
            cancellationToken).ConfigureAwait(false);

        string upstream = result.GetStandardOutputText().Trim();

        return upstream.Length == 0 ? null : upstream;
    }

    private async Task<bool> HasCommitsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", "HEAD"],

                // Doğmamış HEAD hata değil, bilgidir.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }
}
