using Avalonia.Media;
using Avalonia.Styling;
using GitExt.UI.Settings;

namespace GitExt.UI.Themes;

/// <summary>
/// The lane colours of the commit graph (P08-T09).
/// </summary>
/// <remarks>
/// <para>
/// <b>There are four palettes, because two axes are independent:</b> the background (light/dark) and
/// the colour distinction (default / colour-blind safe). A colour that is legible on a light
/// background comes out dull on a dark one; and a colour-blind safe palette has to be tuned
/// separately for each background.
/// </para>
/// <para>
/// <b>The colour-blind safe palette is the Okabe–Ito set</b> — eight widely used colours designed to
/// stay distinguishable under deuteranopia, protanopia and tritanopia. The reason for choosing it
/// over inventing our own mix is simple: "it looked distinguishable to me" is not a verification.
/// </para>
/// <para>
/// <b>Source:</b> Okabe, M. &amp; Ito, K., <i>Color Universal Design</i> (2008).
/// </para>
/// </remarks>
public static class GraphPalettes
{
    /// <summary>
    /// Light background, default palette.
    /// </summary>
    /// <remarks>
    /// Inherited from Phase 03. It uses red and green <b>side by side</b>; for a user with
    /// deuteranopia the two lanes are indistinguishable. That is why an accessible alternative is
    /// essential (below), but the default was left unchanged: so as not to break the familiar look for
    /// no reason.
    /// </remarks>
    public static IReadOnlyList<Color> LightDefault { get; } =
    [
        Color.FromRgb(0x45, 0x7B, 0x9D),
        Color.FromRgb(0xE6, 0x3A, 0x35),
        Color.FromRgb(0x2A, 0x9D, 0x8F),
        Color.FromRgb(0xE9, 0xC4, 0x6A),
        Color.FromRgb(0x8E, 0x7D, 0xBE),
        Color.FromRgb(0xF4, 0xA2, 0x61),
        Color.FromRgb(0x26, 0x46, 0x53),
        Color.FromRgb(0xB5, 0x65, 0x76),
    ];

    /// <summary>
    /// Dark background, default palette.
    /// </summary>
    /// <remarks>
    /// Not the same as the light palette: dark tones such as <c>#264653</c> are <b>invisible</b> on a
    /// dark background. The tones were lightened and the saturation kept.
    /// </remarks>
    public static IReadOnlyList<Color> DarkDefault { get; } =
    [
        Color.FromRgb(0x74, 0xB0, 0xE0),
        Color.FromRgb(0xFF, 0x7B, 0x72),
        Color.FromRgb(0x4E, 0xD8, 0xC2),
        Color.FromRgb(0xF2, 0xD4, 0x8F),
        Color.FromRgb(0xB4, 0xA5, 0xE8),
        Color.FromRgb(0xFF, 0xB1, 0x6E),
        Color.FromRgb(0x8A, 0xB4, 0xC4),
        Color.FromRgb(0xE0, 0x92, 0xA3),
    ];

    /// <summary>Light background, Okabe–Ito.</summary>
    /// <remarks>
    /// <para>
    /// Seven of the colours are canonical Okabe–Ito. The one deviation: the set's yellow
    /// (<c>#F0E442</c>) is all but invisible on a white background at <b>1.1:1</b> contrast; a dark
    /// mustard (<c>#8A6D00</c>, 4.9:1) is used instead.
    /// </para>
    /// <para>
    /// ⚠️ <b>An honest limitation:</b> sky blue (2.3:1) and orange (2.3:1) are <b>below the 3:1</b>
    /// WCAG asks for on non-text elements against white. It was attempted and <b>could not be
    /// solved</b>: darkening the eight colours to reach 3:1 brings them closer together under colour
    /// blindness — measured with a test, the two constraints cannot be met at once.
    /// </para>
    /// <para>
    /// The canonical set was kept because <b>a lane's identity is carried by its column position, not
    /// by its colour</b>; the colour is secondary information. Measured in Phase 03 as well: in real
    /// repositories the number of simultaneous lanes is 2–3, so the fifth and later colours rarely
    /// reach the screen. The limitation is documented in P08-T20.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Color> LightColorBlindSafe { get; } =
    [
        Color.FromRgb(0x00, 0x72, 0xB2),
        Color.FromRgb(0xD5, 0x5E, 0x00),
        Color.FromRgb(0x00, 0x9E, 0x73),
        Color.FromRgb(0xCC, 0x79, 0xA7),
        Color.FromRgb(0x56, 0xB4, 0xE9),
        Color.FromRgb(0xE6, 0x9F, 0x00),
        Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x8A, 0x6D, 0x00),
    ];

    /// <summary>Dark background, Okabe–Ito.</summary>
    /// <remarks>
    /// Dark blue (<c>#0072B2</c>) and black disappear on a dark background; their counterparts are
    /// light blue and light grey. Yellow <b>can be used</b> here — the very one that could not be used
    /// on a light background. In this palette <b>all eight colours</b> have a contrast above 4:1
    /// against the background; the compromise made in the light theme was not needed here.
    /// </remarks>
    public static IReadOnlyList<Color> DarkColorBlindSafe { get; } =
    [
        Color.FromRgb(0x56, 0xB4, 0xE9),
        Color.FromRgb(0xE6, 0x9F, 0x00),
        Color.FromRgb(0x00, 0x9E, 0x73),
        Color.FromRgb(0xCC, 0x79, 0xA7),
        Color.FromRgb(0x8F, 0xD2, 0xFF),
        Color.FromRgb(0xF0, 0xE4, 0x42),
        Color.FromRgb(0xBF, 0xBF, 0xBF),
        Color.FromRgb(0xD5, 0x5E, 0x00),
    ];

    /// <summary>Picks the palette by background and preference.</summary>
    public static IReadOnlyList<Color> Resolve(ThemeVariant variant, PalettePreference preference)
    {
        bool dark = variant == ThemeVariant.Dark;

        return preference switch
        {
            PalettePreference.ColorBlindSafe => dark ? DarkColorBlindSafe : LightColorBlindSafe,
            _ => dark ? DarkDefault : LightDefault,
        };
    }

    /// <summary>The graph palette's key in the resource dictionary.</summary>
    public const string ResourceKey = "GitExtGraphPalette";
}
