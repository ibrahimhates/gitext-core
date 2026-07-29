namespace GitExt.Core.Git;

/// <summary>
/// Bir <c>git</c> çağrısının başarısız olma nedeni.
/// </summary>
/// <remarks>
/// Ham <c>stderr</c> metnini kullanıcıya birincil mesaj olarak göstermek kabul edilebilir değil;
/// bu sınıflandırma, arayüzün anlamlı bir mesaj ve eylem sunabilmesi içindir.
/// Ham metin <see cref="GitException.StandardError"/> üzerinden her zaman erişilebilir kalır.
/// </remarks>
public enum GitFailureKind
{
    /// <summary>Sınıflandırılamadı. Ham <c>stderr</c> gösterilmeli.</summary>
    Unknown,

    /// <summary>Verilen yol bir Git deposu değil.</summary>
    NotARepository,

    /// <summary>Kimlik doğrulama gerekiyor (SSH anahtarı, token, credential helper).</summary>
    AuthenticationRequired,

    /// <summary>Ağ hatası: DNS, bağlantı reddi, zaman aşımı.</summary>
    NetworkFailure,

    /// <summary><c>index.lock</c> mevcut — başka bir git süreci çalışıyor olabilir.</summary>
    IndexLocked,

    /// <summary>Merge/rebase/cherry-pick conflict ile durdu.</summary>
    Conflict,

    /// <summary>Belirtilen revizyon, dal veya ref çözümlenemedi.</summary>
    UnknownRevision,

    /// <summary>Çalışma dizini kirli; işlem devam edemedi.</summary>
    DirtyWorkingTree,

    /// <summary>Süreç zaman aşımına uğradı ve öldürüldü.</summary>
    Timeout,
}

/// <summary>
/// Bir <c>git</c> komutu sıfır olmayan çıkış koduyla döndüğünde fırlatılır.
/// </summary>
public class GitException : Exception
{
    public GitException(
        GitFailureKind kind,
        string message,
        string commandLine,
        int exitCode,
        string standardError)
        : base(message)
    {
        Kind = kind;
        CommandLine = commandLine;
        ExitCode = exitCode;
        StandardError = standardError;
    }

    /// <summary>Hatanın sınıflandırılmış türü.</summary>
    public GitFailureKind Kind { get; }

    /// <summary>Çalıştırılan komut — teşhis ve "komutu göster" ilkesi için.</summary>
    public string CommandLine { get; }

    /// <summary>Sürecin çıkış kodu.</summary>
    public int ExitCode { get; }

    /// <summary>Ham <c>stderr</c> çıktısı. Kullanıcıya detay panelinde gösterilebilir.</summary>
    public string StandardError { get; }
}

/// <summary>
/// Sistemde çalıştırılabilir bir <c>git</c> bulunamadığında fırlatılır.
/// </summary>
public sealed class GitNotFoundException(string message) : Exception(message);

/// <summary>
/// Bulunan <c>git</c> sürümü <see cref="GitVersion.Minimum"/> değerinden düşük olduğunda fırlatılır.
/// </summary>
public sealed class GitVersionTooOldException(GitVersion found, string executablePath)
    : Exception(
        $"Bulunan git sürümü {found} çok eski. En az {GitVersion.Minimum} gerekiyor "
        + $"(çalıştırılabilir: {executablePath}).")
{
    public GitVersion FoundVersion { get; } = found;

    public GitVersion RequiredVersion { get; } = GitVersion.Minimum;

    public string ExecutablePath { get; } = executablePath;
}
