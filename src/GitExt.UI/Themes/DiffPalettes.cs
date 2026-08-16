using Avalonia.Media;
using Avalonia.Styling;

namespace GitExt.UI.Themes;

/// <summary>
/// The colour-blind safe overlay for the diff colours (P08-T09, moved into code in P09-T11).
/// </summary>
/// <remarks>
/// <para>
/// The values are identical to <c>Themes/ColorBlindSafe.axaml</c>; the distinction is <b>blue/orange</b>
/// rather than red/green, because under deuteranopia and protanopia red and green look the same.
/// Colour does not carry the meaning alone either — an added/removed line already has its
/// <c>+</c>/<c>-</c> prefix (P04).
/// </para>
/// <para>
/// 🔴 <b>Why code rather than XAML?</b> The overlay is switched on and off at runtime, and doing that
/// with <c>new ResourceInclude(uri)</c> invokes <c>AvaloniaXamlLoader</c>: the trimmer cannot see which
/// resource will be loaded, and it breaks <c>PublishTrimmed</c> outright with <c>IL2026</c>. Trimming
/// worked in Phase 01 and was silently broken in P08 when this line was added — it surfaced in
/// P09-T04's publish attempt.
/// </para>
/// <para>
/// A <c>ResourceInclude</c> written in XAML is resolved at compile time and is safe; but that route
/// only applies to <b>fixed</b> dictionaries. Moving the values of a toggleable overlay into code gives
/// the same result without breaking trimming.
/// </para>
/// </remarks>
public static class DiffPalettes
{
    /// <summary>The resource keys the overlay overrides.</summary>
    /// <remarks>
    /// Removal goes through this list too: a hand-counted set of keys meant one being forgotten and
    /// silently diverging between the palettes.
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
    /// The colour-blind overlay's brushes for the given theme.
    /// </summary>
    public static IReadOnlyDictionary<string, Color> ColorBlindSafe(ThemeVariant variant) =>
        variant == ThemeVariant.Dark ? DarkColorBlindSafe : LightColorBlindSafe;
}
