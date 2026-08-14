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

    /// <summary>Aynı adda bir dal zaten var (P06-T01).</summary>
    BranchAlreadyExists,

    /// <summary>
    /// Ref adı mevcut bir ref'le dizin/dosya çakışması yaratıyor (P06-T01).
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ — iki yönlü:</b> <c>feature</c> dalı varken <c>feature/x</c> oluşturulamıyor
    /// (<c>refs/heads/feature</c> bir <b>dosya</b>, altına dizin açılamaz) ve simetrik olarak
    /// <c>kap/alt</c> varken <c>kap</c> oluşturulamıyor. Ad kurallarına <b>tamamen uygun</b>
    /// bir ad olduğu için doğrulamadan geçiyor; yalnızca git söyleyebiliyor.
    /// </remarks>
    RefNameConflict,

    /// <summary>Depoda hiç commit yok; işlem bir başlangıç noktası bulamıyor (P06-T01).</summary>
    UnbornHead,

    /// <summary>Aynı adda bir uzak depo zaten var (P06-T05, çıkış kodu 3).</summary>
    RemoteAlreadyExists,

    /// <summary>Belirtilen uzak depo yok (P06-T05, çıkış kodu 2).</summary>
    RemoteNotFound,

    /// <summary>
    /// Uzak depo adı mevcut bir adla iç içe geçiyor (P06-T05).
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ — iki yönlü:</b> <c>ic</c> varken <c>ic/main</c> eklenemiyor
    /// (<i>"is a subset of existing remote"</i>) ve simetrik olarak <c>ic/main</c> varken
    /// <c>ic</c> eklenemiyor (<i>"is a superset"</i>). Dallardaki
    /// <see cref="RefNameConflict"/> ile aynı sebep — <c>refs/remotes/</c> altında da adlar
    /// dizin gibi saklanıyor — ama mesajı ayrı, çünkü kullanıcıya önerilecek çözüm farklı.
    /// </remarks>
    RemoteNameConflict,

    /// <summary>Uzak depoya ulaşılamadı: adres yanlış, depo yok ya da erişim kapalı (P06-T06).</summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — bu tür olmadan mesaj YANLIŞTI.</b> Ulaşılamayan bir remote için git
    /// <c>fatal: '…' does not appear to be a git repository</c> yazıyor; bu metin
    /// <see cref="NotARepository"/> kalıbına uyuyor ve kullanıcıya <i>"Bu klasör bir Git
    /// deposu değil"</i> denirdi — oysa klasör gayet iyi, sorun <b>uzak adreste</b>.
    /// Ayrım git'in ikinci satırından geliyor: <c>Could not read from remote repository.</c>
    /// </remarks>
    RemoteUnreachable,
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
        string standardError,
        string standardOutput = "")
        : base(message)
    {
        Kind = kind;
        CommandLine = commandLine;
        ExitCode = exitCode;
        StandardError = standardError;
        StandardOutput = standardOutput;
    }

    /// <summary>Hatanın sınıflandırılmış türü.</summary>
    public GitFailureKind Kind { get; }

    /// <summary>Çalıştırılan komut — teşhis ve "komutu göster" ilkesi için.</summary>
    public string CommandLine { get; }

    /// <summary>Sürecin çıkış kodu.</summary>
    public int ExitCode { get; }

    /// <summary>Ham <c>stderr</c> çıktısı. Kullanıcıya detay panelinde gösterilebilir.</summary>
    public string StandardError { get; }

    /// <summary>
    /// Ham <c>stdout</c> çıktısı.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>P06-T08'de eklendi — yokluğu sessiz bir bilgi kaybıydı.</b> Başarısız komutların
    /// çoğunda stdout boş olduğu için alan hiç istenmemişti. Ama <c>git push --porcelain</c>
    /// <b>reddedilen</b> ref'leri de <b>stdout'a</b> makine-okunur biçimde yazıp çıkış kodu 1
    /// veriyor (ölçüldü); istisna fırlatılırken bu çıktı atılırsa geriye yalnızca
    /// insan-okunur <c>hint:</c> satırları kalır — yani ADR-0002'nin yasakladığı kanal.
    /// Üstelik kısmi başarıda (bir dal gitti, biri reddedildi) gerçekten <b>ne olduğu</b>
    /// tam olarak burada yazıyor.
    /// </remarks>
    public string StandardOutput { get; }
}

/// <summary>
/// Sistemde çalıştırılabilir bir <c>git</c> bulunamadığında fırlatılır.
/// </summary>
/// <remarks>
/// Flatpak sandbox'ında host'a erişilemediğinde de bu fırlatılıyor (ADR-0009):
/// kullanıcı açısından sonuç aynı — kullanılabilir bir git yok.
/// </remarks>
public sealed class GitNotFoundException(string message, Exception? innerException = null)
    : Exception(message, innerException);

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
