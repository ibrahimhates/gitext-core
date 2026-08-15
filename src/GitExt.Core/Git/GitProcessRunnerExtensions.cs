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

        // 🔴 SIRA ÖNEMLİ — P06-T09'da düzeltildi. SSH tarafında git, kimlik ve ağ
        // hatalarının HEPSİNE "Could not read from remote repository." satırını ekliyor
        // (ölçüldü):
        //   git@github.com: Permission denied (publickey).      -> KİMLİK
        //   ssh: Could not resolve hostname …                   -> AĞ
        // Bu kontrol önce gelseydi (ve P06-T09'a kadar geliyordu) ikisi de
        // "Uzak depo bulunamadı" diye gösterilirdi: kullanıcı adresini kurcalar, oysa
        // adres doğru — eksik olan SSH anahtarı.
        if (ContainsAny(
                text,
                "Authentication failed",
                "could not read Username",
                "could not read Password",
                "Permission denied (publickey",
                "Permission denied, please try again",
                "terminal prompts disabled",
                "Invalid username or token",
                "Support for password authentication was removed"))
        {
            return GitFailureKind.AuthenticationRequired;
        }

        if (ContainsAny(
                text,
                "Could not resolve host",
                "Connection refused",
                "Connection timed out",
                "Network is unreachable",
                "Connection closed by",
                "kex_exchange_identification"))
        {
            return GitFailureKind.NetworkFailure;
        }

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

        // `unable to access` HTTPS tarafının genel ağ hatası; yukarıdaki kimlik
        // kalıplarından SONRA bakılıyor, çünkü kimlik hatası da bu satırı içerebiliyor.
        if (ContainsAny(text, "unable to access"))
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
        GitFailureKind.NotARepository => "This folder is not a Git repository.",
        GitFailureKind.AuthenticationRequired =>
            "The remote asked for authentication. Check your SSH key, your credential helper "
            + "or your access token.",
        GitFailureKind.NetworkFailure => "The remote could not be reached. Check your network connection.",
        GitFailureKind.IndexLocked =>
            "The repository is locked. Another Git process may be running; try again in a few "
            + "seconds. If the lock has been there for a long time it may be left over from a crashed process "
            + "olabilir.",
        GitFailureKind.Conflict => "The operation stopped because of a conflict.",
        GitFailureKind.UnknownRevision => "No such revision or branch.",
        GitFailureKind.DirtyWorkingTree =>
            "There are uncommitted changes in the working directory; the operation could not continue.",
        GitFailureKind.Timeout => "The command timed out.",
        GitFailureKind.BranchAlreadyExists => "Bu adda bir dal zaten var.",
        GitFailureKind.RefNameConflict =>
            "This name conflicts with an existing branch. Git stores branches like files: "
            + "with a \"feature\" branch present you cannot create \"feature/x\" (and vice versa).",
        GitFailureKind.RemoteAlreadyExists => "Bu adda bir uzak depo zaten var.",
        GitFailureKind.RemoteNotFound => "No such remote.",
        GitFailureKind.RemoteUnreachable =>
            "The remote could not be reached. Check the address, your network connection and your access rights.",
        GitFailureKind.RemoteNameConflict =>
            "This name nests with an existing remote: with \"ic\" present, \"ic/main\" "
            + "cannot be added (and vice versa). Choose a different name.",
        GitFailureKind.UnbornHead =>
            "There are no commits in the repository yet, so there is no starting point for a branch. "
            + "Make the first commit first.",
        _ => "The git command failed.",
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
