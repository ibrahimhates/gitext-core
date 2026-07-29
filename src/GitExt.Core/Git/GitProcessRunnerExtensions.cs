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
            result.StandardError);
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

        if (ContainsAny(text, "not a git repository", "does not appear to be a git repository"))
        {
            return GitFailureKind.NotARepository;
        }

        if (ContainsAny(text, "index.lock", "Another git process seems to be running"))
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

        if (ContainsAny(
                text,
                "unknown revision or path not in the working tree",
                "bad revision",
                "ambiguous argument",
                "not a valid object name"))
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
            "Depo kilitli (index.lock). Başka bir Git süreci çalışıyor olabilir.",
        GitFailureKind.Conflict => "İşlem çakışma (conflict) nedeniyle durdu.",
        GitFailureKind.UnknownRevision => "Belirtilen revizyon veya dal bulunamadı.",
        GitFailureKind.DirtyWorkingTree =>
            "Çalışma dizininde kaydedilmemiş değişiklikler var; işlem devam edemedi.",
        GitFailureKind.Timeout => "Komut zaman aşımına uğradı.",
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
