using GitExt.Core.Model;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T13 — Gösterim dönüşümleri: sekme açma ve boşluk gösterimi.
/// </summary>
/// <remarks>
/// <b>ÖLÇÜLDÜ:</b> Avalonia'nın <c>TextBlock</c>'unda sekme <b>tab-stop değil</b>, sabit dört
/// boşluk genişliğinde çiziliyor ve ayarlanamıyor. Dönüşüm bu yüzden burada yapılıyor.
/// </remarks>
public class DiffTextFormatterTests
{
    private static DiffTextOptions Tabs(int width = 4) => new() { TabWidth = width };

    [Fact]
    public void Sekme_tab_stop_a_kadar_doldurur()
    {
        // Kritik ayrım: sabit genişlik DEĞİL. "ab" iki sütun, sonraki durak 4 → iki boşluk.
        DiffTextFormatter.Format("ab\tc", Tabs()).ShouldBe("ab  c");

        // "a" bir sütun → üç boşluk.
        DiffTextFormatter.Format("a\tb", Tabs()).ShouldBe("a   b");

        // Tam durakta olan sekme TAM genişlik ilerletir (sıfır değil).
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

        // "a b" üç sütun; sonraki durak 4 olduğu için sekme YALNIZCA bir sütun ilerletir,
        // yani işaretten sonra boşluk kalmaz. (İlk beklentim yanlıştı; doğrusu ölçülen bu.)
        result.ShouldBe($"a{DiffTextFormatter.SpaceMarker}b{DiffTextFormatter.TabMarker}c");

        // İşaretten sonra dolgu kaldığı durum:
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
        // ⚠️ Asıl tuzak bu: tab-stop satırın BAŞINDAN hesaplanır. Sayaç her parçada
        // sıfırlansaydı satır içi vurgulaması olan satırlarda sekmeler farklı yere
        // hizalanır ve iki satır görsel olarak kaymış görünürdü.
        DiffSegment[] segments =
        [
            new(DiffLineKind.Context, "ab"),
            new(DiffLineKind.Added, "\tc"),
        ];

        IReadOnlyList<DiffSegment> result = DiffTextFormatter.Format(segments, Tabs());

        result[0].Text.ShouldBe("ab");

        // "ab" iki sütun tuttuğu için sekme yalnızca iki boşluk açmalı.
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
        // Satır sayısı on binlere çıkabiliyor; gereksiz dize üretmemek önemli.
        DiffSegment[] segments = [new(DiffLineKind.Context, "sekmesiz metin")];

        DiffTextFormatter.Format(segments, new DiffTextOptions { TabWidth = 0 })
            .ShouldBeSameAs(segments);
    }
}
