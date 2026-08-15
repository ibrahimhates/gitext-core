using GitExt.Core.Model;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T13 — Display transformations: tab expansion and whitespace rendering.
/// </summary>
/// <remarks>
/// <b>MEASURED:</b> in Avalonia's <c>TextBlock</c> a tab is <b>not a tab stop</b>, it is drawn at a
/// fixed width of four spaces and cannot be configured. That is why the transformation is done here.
/// </remarks>
public class DiffTextFormatterTests
{
    private static DiffTextOptions Tabs(int width = 4) => new() { TabWidth = width };

    [Fact]
    public void Sekme_tab_stop_a_kadar_doldurur()
    {
        // The critical distinction: NOT a fixed width. "ab" is two columns, the next stop is 4 → two spaces.
        DiffTextFormatter.Format("ab\tc", Tabs()).ShouldBe("ab  c");

        // "a" is one column → three spaces.
        DiffTextFormatter.Format("a\tb", Tabs()).ShouldBe("a   b");

        // A tab exactly on a stop advances a FULL width (not zero).
        DiffTextFormatter.Format("abcd\te", Tabs()).ShouldBe("abcd    e");
    }

    [Fact]
    public void Sekme_genisligi_ayarlanabilir()
    {
        DiffTextFormatter.Format("a\tb", Tabs(8)).ShouldBe("a       b");
        DiffTextFormatter.Format("a\tb", Tabs(2)).ShouldBe("a b");
    }

    [Fact]
    public void Ardisik_sekmeler_dogru_hizalanir()
    {
        DiffTextFormatter.Format("\t\tx", Tabs()).ShouldBe("        x");
    }

    [Fact]
    public void Bosluk_gosterimi_acikken_isaretler_konur()
    {
        DiffTextOptions options = new() { TabWidth = 4, ShowWhitespace = true };

        string result = DiffTextFormatter.Format("a b\tc", options);

        // "a b" is three columns; since the next stop is 4 the tab advances ONLY one column,
        // so no space remains after the marker. (My first expectation was wrong; this is what was measured.)
        result.ShouldBe($"a{DiffTextFormatter.SpaceMarker}b{DiffTextFormatter.TabMarker}c");

        // The case where padding remains after the marker:
        DiffTextFormatter.Format("a\tb", options)
            .ShouldBe($"a{DiffTextFormatter.TabMarker}  b");
    }

    [Fact]
    public void Kapaliyken_metin_aynen_kalir()
    {
        DiffTextOptions off = new() { TabWidth = 0, ShowWhitespace = false };

        DiffTextFormatter.Format("a\tb c", off).ShouldBe("a\tb c");
    }

    [Fact]
    public void Parcalar_arasinda_sutun_sayaci_devam_eder()
    {
        // ⚠️ This is the real trap: the tab stop is computed from the START of the line. If the counter
        // were reset in each segment, tabs on lines with intra-line highlighting would align to different
        // places and two lines would look visually shifted.
        DiffSegment[] segments =
        [
            new(DiffLineKind.Context, "ab"),
            new(DiffLineKind.Added, "\tc"),
        ];

        IReadOnlyList<DiffSegment> result = DiffTextFormatter.Format(segments, Tabs());

        result[0].Text.ShouldBe("ab");

        // Since "ab" occupies two columns, the tab must open only two spaces.
        result[1].Text.ShouldBe("  c");
    }

    [Fact]
    public void Parca_turleri_korunur()
    {
        DiffSegment[] segments =
        [
            new(DiffLineKind.Context, "bir "),
            new(DiffLineKind.Added, "iki"),
        ];

        IReadOnlyList<DiffSegment> result = DiffTextFormatter.Format(
            segments,
            new DiffTextOptions { TabWidth = 4, ShowWhitespace = true });

        result[0].Kind.ShouldBe(DiffLineKind.Context);
        result[1].Kind.ShouldBe(DiffLineKind.Added);
        result[1].Text.ShouldBe("iki");
    }

    [Fact]
    public void Degisiklik_gerekmiyorsa_ayni_liste_dondurulur()
    {
        // The line count can reach tens of thousands; not producing unnecessary strings matters.
        DiffSegment[] segments = [new(DiffLineKind.Context, "sekmesiz metin")];

        DiffTextFormatter.Format(segments, new DiffTextOptions { TabWidth = 0 })
            .ShouldBeSameAs(segments);
    }
}
