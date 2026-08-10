using Avalonia.Media;
using Avalonia.Styling;

namespace GitExt.UI.Themes;

/// <summary>
/// Diff renklerinin renk körlüğü uyumlu kaplaması (P08-T09, P09-T11'de koda taşındı).
/// </summary>
/// <remarks>
/// <para>
/// Değerler <c>Themes/ColorBlindSafe.axaml</c> ile birebir aynı; ayrım kırmızı/yeşil
/// yerine <b>mavi/turuncu</b>, çünkü deuteranopi ve protanopide kırmızı ile yeşil aynı
/// görünüyor. Renk tek başına anlam da taşımıyor — eklenen/çıkarılan satırın
/// <c>+</c>/<c>-</c> öneki zaten var (P04).
/// </para>
/// <para>
/// 🔴 <b>Neden XAML değil de kod?</b> Kaplama çalışma zamanında açılıp kapanıyor ve bunu
/// <c>new ResourceInclude(uri)</c> ile yapmak <c>AvaloniaXamlLoader</c>'ı çağırıyor:
/// trimmer hangi kaynağın yükleneceğini göremiyor ve <c>IL2026</c> ile
/// <c>PublishTrimmed</c>'i tamamen kırıyor. Faz 01'de çalışan trimming, P08'de bu satır
/// eklendiğinde sessizce bozulmuştu — P09-T04'ün publish denemesinde ortaya çıktı.
/// </para>
/// <para>
/// XAML'de yazılan <c>ResourceInclude</c> derleme zamanında çözülüyor ve güvenli; ama o
/// yol yalnızca <b>sabit</b> sözlükler için geçerli. Açılıp kapanan bir kaplamanın
/// değerlerini koda taşımak, aynı sonucu trimming'i kırmadan veriyor.
/// </para>
/// </remarks>
public static class DiffPalettes
{
    /// <summary>Kaplamanın ezdiği kaynak anahtarları.</summary>
    /// <remarks>
    /// Kaldırma da bu liste üzerinden yapılıyor: elle sayılan bir anahtar kümesi,
    /// birinin unutulup paletler arası sessizce ayrışması demekti.
    /// </remarks>
    public static IReadOnlyList<string> OverlayKeys { get; } =
    [
        "GitExtSuccessBrush",
        "GitExtDangerBrush",
        "GitExtDiffAddedBackgroundBrush",
        "GitExtDiffRemovedBackgroundBrush",
        "GitExtDiffAddedForegroundBrush",
        "GitExtDiffRemovedForegroundBrush",
        "GitExtDiffAddedWordBrush",
        "GitExtDiffRemovedWordBrush",
    ];

    private static readonly IReadOnlyDictionary<string, Color> LightColorBlindSafe =
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["GitExtSuccessBrush"] = Color.FromRgb(0x0B, 0x5F, 0xBF),
            ["GitExtDangerBrush"] = Color.FromRgb(0xB3, 0x5C, 0x00),
            ["GitExtDiffAddedBackgroundBrush"] = Color.FromRgb(0xDD, 0xEB, 0xFF),
            ["GitExtDiffRemovedBackgroundBrush"] = Color.FromRgb(0xFF, 0xEB, 0xD6),
            ["GitExtDiffAddedForegroundBrush"] = Color.FromRgb(0x0B, 0x5F, 0xBF),
            ["GitExtDiffRemovedForegroundBrush"] = Color.FromRgb(0xB3, 0x5C, 0x00),
            ["GitExtDiffAddedWordBrush"] = Color.FromRgb(0xB8, 0xD4, 0xFF),
            ["GitExtDiffRemovedWordBrush"] = Color.FromRgb(0xFF, 0xD4, 0xA8),
        };

    private static readonly IReadOnlyDictionary<string, Color> DarkColorBlindSafe =
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["GitExtSuccessBrush"] = Color.FromRgb(0x6C, 0xB6, 0xFF),
            ["GitExtDangerBrush"] = Color.FromRgb(0xE3, 0xA8, 0x57),
            ["GitExtDiffAddedBackgroundBrush"] = Color.FromRgb(0x10, 0x23, 0x3C),
            ["GitExtDiffRemovedBackgroundBrush"] = Color.FromRgb(0x33, 0x23, 0x0F),
            ["GitExtDiffAddedForegroundBrush"] = Color.FromRgb(0x6C, 0xB6, 0xFF),
            ["GitExtDiffRemovedForegroundBrush"] = Color.FromRgb(0xE3, 0xA8, 0x57),
            ["GitExtDiffAddedWordBrush"] = Color.FromRgb(0x1E, 0x3C, 0x63),
            ["GitExtDiffRemovedWordBrush"] = Color.FromRgb(0x58, 0x41, 0x1A),
        };

    /// <summary>
    /// Verilen tema için renk körlüğü kaplamasının fırçaları.
    /// </summary>
    public static IReadOnlyDictionary<string, Color> ColorBlindSafe(ThemeVariant variant) =>
        variant == ThemeVariant.Dark ? DarkColorBlindSafe : LightColorBlindSafe;
}
