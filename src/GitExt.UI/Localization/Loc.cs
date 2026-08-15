using System.Globalization;
using GitExt.Core.Git;

namespace GitExt.UI.Localization;

/// <summary>
/// Kod içinden çeviri erişimi (P11-T05).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Bu bir Service Locator değil — ama sınırı burada çizmek gerekiyor.</b>
/// ADR-0004 Service Locator'ı yasaklıyor ve gerekçesi şu: bağımlılıklar gizlenirse bir
/// sınıfın neye ihtiyaç duyduğu yapıcısına bakılarak anlaşılamaz. Çeviri bu kuralın
/// dışında tutuluyor, çünkü:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Uygulama genelinde tek ve değişmez bir kaynak.</b> Test için sahtelenmesi
///     gereken bir davranışı yok — <see cref="UseForTesting"/> ile değiştirilebiliyor.
///   </item>
///   <item>
///     <b>Alternatifin maliyeti gerçek:</b> 22 ViewModel'in yapıcısına bir parametre daha
///     eklemek, hepsinin çağrı yerlerini ve testlerini değiştirmek demekti — hiçbiri
///     çeviriyi <i>farklı</i> yapmayacakken.
///   </item>
///   <item>
///     Aynı gerekçe <see cref="TranslateExtension"/> için de geçerli ve orada teknik bir
///     zorunluluktu (markup extension'ı XAML çözümleyici yaratıyor).
///   </item>
/// </list>
/// <para>
/// <b>Kural:</b> buraya yalnızca <b>kullanıcıya gösterilen metin</b> için başvurulur.
/// Başka bir servise bu yoldan erişilmez.
/// </para>
/// </remarks>
public static class Loc
{
    private static ITranslator? _translator;

    /// <summary>
    /// The text for a key. Falls back to built-in English when no translator is attached.
    /// </summary>
    /// <remarks>
    /// 🔴 This used to return the <b>key name</b> when no translator was attached, and any code
    /// path that runs without the composition root — plain <c>[Fact]</c> tests, the XAML
    /// designer — showed raw keys like <c>git_output.commit_created</c>. A test caught it.
    /// <see cref="BuiltInEnglish"/> is compiled in and depends on no file, so there is no
    /// reason to ever show a key to anyone.
    /// </remarks>
    public static string T(string key) =>
        _translator is not null
            ? _translator[key]
            : BuiltInEnglish.Entries.GetValueOrDefault(key, key);

    /// <summary>Fills in a placeholder text.</summary>
    public static string F(string key, params object?[] arguments)
    {
        if (_translator is not null)
        {
            return _translator.Format(key, arguments);
        }

        string template = T(key);

        if (arguments is not { Length: > 0 })
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            // A broken placeholder in a translation must not crash the application.
            return template;
        }
    }

    /// <summary>Etkin çevirmeni tanıtır. Composition root'tan bir kez çağrılıyor.</summary>
    public static void Attach(ITranslator translator) => _translator = translator;

    /// <summary>Testlerin çevirmeni değiştirmesi için.</summary>
    internal static void UseForTesting(ITranslator? translator) => _translator = translator;

    /// <summary>
    /// Bir git hatasının kullanıcıya gösterilecek metni (P11-T06).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GitExt.Core"/> arayüz katmanına bağlanamıyor (ADR-0003), dolayısıyla
    /// dil dosyasına da erişemiyor. Ama <see cref="GitException.Kind"/> zaten sınıflandırma
    /// katmanı tarafından dolduruluyor: çeviri <b>metne değil bu enum'a</b> bakıyor.
    /// </para>
    /// <para>
    /// <see cref="GitFailureKind.Unknown"/> <b>ham mesaja düşüyor.</b> Bilinmeyen bir git
    /// hatasını uydurma bir metnin arkasına saklamak, teşhisi imkânsızlaştırırdı — kullanıcı
    /// da biz de git'in ne dediğini görmek zorundayız.
    /// </para>
    /// </remarks>
    public static string GitError(GitException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string key = exception.Kind switch
        {
            GitFailureKind.NotARepository => "git.error.not_a_repository",
            GitFailureKind.AuthenticationRequired => "git.error.authentication_required",
            GitFailureKind.NetworkFailure => "git.error.network_failure",
            GitFailureKind.IndexLocked => "git.error.index_locked",
            GitFailureKind.Conflict => "git.error.conflict",
            GitFailureKind.UnknownRevision => "git.error.unknown_revision",
            GitFailureKind.DirtyWorkingTree => "git.error.dirty_working_tree",
            GitFailureKind.Timeout => "git.error.timeout",
            GitFailureKind.BranchAlreadyExists => "git.error.branch_already_exists",
            GitFailureKind.RefNameConflict => "git.error.ref_name_conflict",
            GitFailureKind.UnbornHead => "git.error.unborn_head",
            GitFailureKind.RemoteAlreadyExists => "git.error.remote_already_exists",
            GitFailureKind.RemoteNotFound => "git.error.remote_not_found",
            GitFailureKind.RemoteNameConflict => "git.error.remote_name_conflict",
            GitFailureKind.RemoteUnreachable => "git.error.remote_unreachable",

            // Sınıflandırılamamış hata: git'in kendi mesajı gösteriliyor.
            _ => string.Empty,
        };

        return key.Length == 0 ? exception.Message : T(key);
    }
}
