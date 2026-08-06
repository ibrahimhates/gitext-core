namespace GitExt.Core.Git;

/// <summary>
/// <see cref="IGitProcessRunner"/> için kolaylık uzantıları.
/// </summary>
public static class GitProcessRunnerExtensions
{
    /// <summary>
    /// Komutu çalıştırır; başarısız olursa sınıflandırılmış bir <see cref="GitException"/> fırlatır.
    /// </summary>
    public static async Task<GitResult> RunCheckedAsync(
        this IGitProcessRunner runner,
        GitCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);

        GitResult result = await runner.RunAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return result;
        }

        GitFailureKind kind = GitFailureClassifier.Classify(result.StandardError);

        throw new GitException(
            kind,
            GitFailureClassifier.Describe(kind),
            command.ToDisplayString(),
            result.ExitCode,
            result.StandardError,
            result.GetStandardOutputText());
    }

    /// <summary>
    /// Komutu çalıştırır ve stdout'u kırpılmış metin olarak döndürür.
    /// </summary>
    /// <remarks>
    /// Tek satırlık çıktı veren komutlar için (<c>rev-parse</c>, <c>config --get</c> gibi).
    /// </remarks>
    public static async Task<string> RunForTextAsync(
        this IGitProcessRunner runner,
        GitCommand command,
        CancellationToken cancellationToken = default)
    {
        GitResult result = await runner.RunCheckedAsync(command, cancellationToken).ConfigureAwait(false);
        return result.GetStandardOutputText().TrimEnd('\n', '\r');
    }
}

/// <summary>
/// <c>git</c> hata çıktısını anlamlı bir türe eşler (P02-T12).
/// </summary>
/// <remarks>
/// Eşleme <c>stderr</c> metnine bakar. Bu kaçınılmaz olarak kırılgandır — bu yüzden
/// eşleşme bulunamazsa <see cref="GitFailureKind.Unknown"/> döner ve ham metin kullanıcıya
/// gösterilir. Yanlış sınıflandırmaktansa sınıflandırmamak yeğdir.
/// <para>
/// <c>LC_ALL=C</c> her çağrıda ayarlandığı için (ADR-0002) bu metinler kullanıcının diline
/// göre değişmez; aksi halde bu eşleme hiç çalışmazdı.
/// </para>
/// </remarks>
internal static class GitFailureClassifier
{
    internal static GitFailureKind Classify(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return GitFailureKind.Unknown;
        }

        ReadOnlySpan<char> text = standardError.AsSpan();

        // ⚠️ SIRA ÖNEMLİ: bu kontrol aşağıdaki "does not appear to be a git repository"
        // kalıbından ÖNCE gelmeli. Ulaşılamayan bir remote için git ikisini birden yazıyor
        // ve genel kalıba düşseydi kullanıcıya "Bu klasör bir Git deposu değil" derdik —
        // klasör iyiyken (P06-T06'da ölçüldü).
        if (ContainsAny(text, "Could not read from remote repository"))
        {
            return GitFailureKind.RemoteUnreachable;
        }

        if (ContainsAny(text, "not a git repository", "does not appear to be a git repository"))
        {
            return GitFailureKind.NotARepository;
        }

        // ÖLÇÜLDÜ (P05-T02): kilit çakışmasının iki farklı mesaj biçimi var —
        //   index:  fatal: Unable to create '…/index.lock': File exists.
        //   ref:    fatal: cannot lock ref 'HEAD': Unable to create '…/main.lock': File exists.
        // İkincisinde "index.lock" GEÇMİYOR, ama git her ikisine de
        // "Another git process seems to be running…" satırını ekliyor; bu yüzden aşağıdaki
        // iki kalıp yetiyor. (Ref kilidi için ayrıca "cannot lock ref" kalıbı denendi ve
        // GEREKSİZ olduğu görüldü — test onsuz da geçiyor.)
        if (ContainsAny(
                text,
                "index.lock",
                "Another git process seems to be running"))
        {
            return GitFailureKind.IndexLocked;
        }

        if (ContainsAny(
                text,
                "Authentication failed",
                "could not read Username",
                "could not read Password",
                "Permission denied (publickey",
                "terminal prompts disabled"))
        {
            return GitFailureKind.AuthenticationRequired;
        }

        if (ContainsAny(
                text,
                "Could not resolve host",
                "Connection refused",
                "Connection timed out",
                "unable to access",
                "Network is unreachable"))
        {
            return GitFailureKind.NetworkFailure;
        }

        if (ContainsAny(text, "CONFLICT", "Automatic merge failed", "needs merge"))
        {
            return GitFailureKind.Conflict;
        }

        // ⚠️ SIRA ÖNEMLİ: "error: remote origin already exists." aşağıdaki genel
        // "already exists" kalıbına da uyuyor — remote kontrolü ÖNCE gelmeli, yoksa uzak
        // depo çakışması kullanıcıya "Bu adda bir dal zaten var." derdi (P06-T05).
        if (ContainsAny(text, "remote ") && ContainsAny(text, "already exists"))
        {
            return GitFailureKind.RemoteAlreadyExists;
        }

        // ÖLÇÜLDÜ (P06-T05): iki yazım da geçiyor — `remove`/`rename` iki nokta üst üste
        // ile ("No such remote: 'x'"), `get-url`/`set-url` onsuz ("No such remote 'x'").
        if (ContainsAny(text, "No such remote"))
        {
            return GitFailureKind.RemoteNotFound;
        }

        // ÖLÇÜLDÜ (P06-T05): "fatal: remote name 'ic/main' is a subset of existing remote 'ic'"
        if (ContainsAny(text, "is a subset of existing remote", "is a superset of existing remote"))
        {
            return GitFailureKind.RemoteNameConflict;
        }

        // ⚠️ Sıra önemli: aşağıdaki iki kalıp "cannot lock ref" içerebiliyor ama yukarıdaki
        // kilit kontrolü yalnızca "index.lock" / "Another git process…" arıyor, o yüzden
        // birbirlerini yemiyorlar (P06-T01'de ölçüldü).
        if (ContainsAny(text, "already exists"))
        {
            return GitFailureKind.BranchAlreadyExists;
        }

        // ÖLÇÜLDÜ: "cannot lock ref 'refs/heads/feature/x': 'refs/heads/feature' exists;
        //           cannot create 'refs/heads/feature/x'"
        if (ContainsAny(text, "cannot create", "is not a valid ref name"))
        {
            return GitFailureKind.RefNameConflict;
        }

        if (ContainsAny(
                text,
                "unknown revision or path not in the working tree",
                "bad revision",
                "ambiguous argument",
                "not a valid object name",

                // ÖLÇÜLDÜ (P06-T02): `git switch` çözümlenemeyen hedefte bu METNİ
                // kullanıyor, yukarıdakilerin hiçbirini değil.
                "invalid reference"))
        {
            return GitFailureKind.UnknownRevision;
        }

        if (ContainsAny(
                text,
                "Your local changes to the following files would be overwritten",
                "cannot pull with rebase: You have unstaged changes",
                "Please commit your changes or stash them"))
        {
            return GitFailureKind.DirtyWorkingTree;
        }

        return GitFailureKind.Unknown;
    }

    /// <summary>
    /// Tür için kullanıcıya gösterilebilecek bir açıklama üretir.
    /// </summary>
    internal static string Describe(GitFailureKind kind) => kind switch
    {
        GitFailureKind.NotARepository => "Bu klasör bir Git deposu değil.",
        GitFailureKind.AuthenticationRequired =>
            "Uzak depo kimlik doğrulama istedi. SSH anahtarınızı, kimlik yardımcınızı (credential helper) "
            + "veya erişim belirtecinizi kontrol edin.",
        GitFailureKind.NetworkFailure => "Uzak depoya ulaşılamadı. Ağ bağlantınızı kontrol edin.",
        GitFailureKind.IndexLocked =>
            "Depo kilitli. Başka bir Git süreci çalışıyor olabilir; birkaç saniye sonra "
            + "tekrar deneyin. Kilit uzun süredir duruyorsa çökmüş bir süreçten kalmış "
            + "olabilir.",
        GitFailureKind.Conflict => "İşlem çakışma (conflict) nedeniyle durdu.",
        GitFailureKind.UnknownRevision => "Belirtilen revizyon veya dal bulunamadı.",
        GitFailureKind.DirtyWorkingTree =>
            "Çalışma dizininde kaydedilmemiş değişiklikler var; işlem devam edemedi.",
        GitFailureKind.Timeout => "Komut zaman aşımına uğradı.",
        GitFailureKind.BranchAlreadyExists => "Bu adda bir dal zaten var.",
        GitFailureKind.RefNameConflict =>
            "Bu ad mevcut bir dalla çakışıyor. Git dalları dosya gibi saklar: "
            + "\"feature\" dalı varken \"feature/x\" oluşturulamaz (ve tersi).",
        GitFailureKind.RemoteAlreadyExists => "Bu adda bir uzak depo zaten var.",
        GitFailureKind.RemoteNotFound => "Böyle bir uzak depo yok.",
        GitFailureKind.RemoteUnreachable =>
            "Uzak depoya ulaşılamadı. Adresi, ağ bağlantınızı ve erişim yetkinizi kontrol edin.",
        GitFailureKind.RemoteNameConflict =>
            "Bu ad mevcut bir uzak depoyla iç içe geçiyor: \"ic\" varken \"ic/main\" "
            + "eklenemez (ve tersi). Farklı bir ad seçin.",
        GitFailureKind.UnbornHead =>
            "Depoda henüz commit yok, dal oluşturulacak bir başlangıç noktası bulunamıyor. "
            + "Önce ilk commit'i atın.",
        _ => "Git komutu başarısız oldu.",
    };

    private static bool ContainsAny(ReadOnlySpan<char> text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
