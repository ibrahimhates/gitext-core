using Avalonia.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// Kısayolun geçerli olduğu bağlam (P08-T01).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Bağlam süs değil, zorunluluk.</b> P08-T00'da ölçüldü: <c>Window.KeyBindings</c>'e konan
/// bir jest <b>koşulsuz küreseldir</b> — odaklı kontrol onu ne görebilir (M12: kontrol
/// <c>Handled=true</c> yapsa bile komut yine çalışıyor) ne de kendine saklayabilir
/// (M11: <c>Down</c> bağlanınca <c>ListBox</c> seçimi hiç kıpırdamadı).
/// </para>
/// <para>
/// Bu yüzden ok tuşları, çıplak harfler ve düzenleme tuşları <b>asla</b> <see cref="Global"/>
/// olmaz; bir bağlama bağlanır ve o bağlamın görünümü onları kendi tünelleyen
/// işleyicisinden dağıtır.
/// </para>
/// </remarks>
[Flags]
public enum CommandContext
{
    /// <summary>Hiçbir yerde — yalnızca komut paletinden çağrılabilir.</summary>
    None = 0,

    /// <summary>Uygulama açıkken her yerde. <b>Metin kutusundayken de çalışır.</b></summary>
    Global = 1,

    /// <summary>Commit listesi odaktayken.</summary>
    CommitList = 2,

    /// <summary>Çalışma ağacı (staging) paneli odaktayken.</summary>
    WorkingTree = 4,

    /// <summary>Diff görünümü odaktayken.</summary>
    Diff = 8,

    /// <summary>Dal/ref paneli odaktayken.</summary>
    RefTree = 16,
}

/// <summary>
/// Kısayol ekranında ve komut paletinde gruplama için kategori.
/// </summary>
public enum CommandCategory
{
    Repository,
    Commit,
    Branch,
    Remote,
    History,
    View,
    Navigation,
    Tools,
    Help,
}

/// <summary>
/// Bir komutun <b>kalıcı</b> tanımı (P08-T01).
/// </summary>
/// <param name="Id">
/// Kalıcı kimlik. <b>Asla değişmez</b>: ayar dosyasında kullanıcının yeniden atadığı
/// kısayolların anahtarı budur; değişirse kullanıcı atamalarını sessizce kaybeder.
/// </param>
/// <param name="Title">Menüde, palette ve kısayol ekranında görünen ad.</param>
/// <param name="Category">Gruplama.</param>
/// <param name="Context">Kısayolun geçerli olduğu bağlam(lar).</param>
/// <param name="DefaultGesture">
/// Varsayılan kısayol. <see langword="null"/> ise komutun varsayılan kısayolu yoktur
/// (yalnızca menü/palet). Kullanıcı sonradan atayabilir.
/// </param>
public sealed record CommandDefinition(
    string Id,
    string Title,
    CommandCategory Category,
    CommandContext Context,
    KeyGesture? DefaultGesture)
{
    /// <summary>
    /// İki bağlamın <b>aynı anda etkin olabilip olamayacağı</b>.
    /// </summary>
    /// <remarks>
    /// Çakışma tespitinin çekirdeği. <see cref="CommandContext.Global"/> her şeyle çakışır;
    /// iki farklı panel bağlamı (ör. commit listesi ve çalışma ağacı) <b>çakışmaz</b> —
    /// odak ikisinde birden olamaz, dolayısıyla aynı jest ikisinde farklı iş yapabilir.
    /// GitExtensions'ta da böyle: <c>Ctrl+D</c> panelden panele farklı anlam taşır.
    /// </remarks>
    public static bool ContextsOverlap(CommandContext left, CommandContext right) =>
        left.HasFlag(CommandContext.Global)
        || right.HasFlag(CommandContext.Global)
        || (left & right) != CommandContext.None;
}

/// <summary>Aynı jesti paylaşan ve bağlamları örtüşen komut çifti.</summary>
/// <param name="Gesture">Çakışan jest.</param>
/// <param name="CommandIds">Çakışan komutların kimlikleri, tanım sırasında.</param>
public sealed record ShortcutConflict(KeyGesture Gesture, IReadOnlyList<string> CommandIds);
