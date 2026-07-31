using GitExt.Core.Model;

namespace GitExt.Core.Tests;

/// <summary>
/// P04-T10 — Yan yana yerleşim.
/// </summary>
/// <remarks>
/// Buradaki asıl koruma <b>projeksiyon değişmezi</b>: sol sütun yukarıdan aşağı okununca
/// dosyanın eski hâli, sağ sütun okununca yeni hâli çıkmalı. Hizalama bozulursa satırlar
/// hâlâ ekranda görünür ama <b>yanlış yerde</b> olur; tek dürüst kontrol budur.
/// </remarks>
public class SideBySideDiffTests
{
    private static DiffHunk Hunk(params DiffLine[] lines) =>
        new()
        {
            Header = "@@ -1,3 +1,3 @@",
            OldStart = 1,
            OldLength = lines.Count(l => l.Kind != DiffLineKind.Added),
            NewStart = 1,
            NewLength = lines.Count(l => l.Kind != DiffLineKind.Removed),
            Lines = lines,
        };

    private static DiffLine Context(string text) => new(DiffLineKind.Context, text);

    private static DiffLine Removed(string text) => new(DiffLineKind.Removed, text);

    private static DiffLine Added(string text) => new(DiffLineKind.Added, text);

    private static string[] LeftColumn(IReadOnlyList<SideBySideRow> rows) =>
        [.. rows.Where(r => !r.IsHunkHeader && r.Left is not null).Select(r => r.Left!.Content)];

    private static string[] RightColumn(IReadOnlyList<SideBySideRow> rows) =>
        [.. rows.Where(r => !r.IsHunkHeader && r.Right is not null).Select(r => r.Right!.Content)];

    [Fact]
    public void Baglam_satiri_iki_tarafta_da_gorunur()
    {
        IReadOnlyList<SideBySideRow> rows = SideBySideDiff.Build(Hunk(
            Context("bir"),
            Context("iki")));

        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(r => r.Left != null && r.Right != null);
    }

    [Fact]
    public void Esit_sayida_degisiklik_yan_yana_hizalanir()
    {
        IReadOnlyList<SideBySideRow> rows = SideBySideDiff.Build(Hunk(
            Context("bir"),
            Removed("iki eski"),
            Removed("uc eski"),
            Added("iki yeni"),
            Added("uc yeni"),
            Context("dort")));

        rows.Count.ShouldBe(4);

        rows[1].Left!.Content.ShouldBe("iki eski");
        rows[1].Right!.Content.ShouldBe("iki yeni");

        rows[2].Left!.Content.ShouldBe("uc eski");
        rows[2].Right!.Content.ShouldBe("uc yeni");
    }

    [Fact]
    public void Yalnizca_eklenen_satirin_solu_bostur()
    {
        IReadOnlyList<SideBySideRow> rows = SideBySideDiff.Build(Hunk(
            Context("bir"),
            Added("yeni satir")));

        rows[1].Left.ShouldBeNull();
        rows[1].Right!.Content.ShouldBe("yeni satir");
    }

    [Fact]
    public void Yalnizca_silinen_satirin_sagi_bostur()
    {
        IReadOnlyList<SideBySideRow> rows = SideBySideDiff.Build(Hunk(
            Context("bir"),
            Removed("giden satir")));

        rows[1].Left!.Content.ShouldBe("giden satir");
        rows[1].Right.ShouldBeNull();
    }

    [Fact]
    public void Eslesmeyen_satirlar_yan_yana_KONULMAZ()
    {
        // Eşleme algoritması bu ikisinin karşılıklı olmadığına karar verdiyse, yan yana
        // koymak kullanıcıya "bunlar karşılıklı" demek olurdu. Yer tasarrufu için
        // doğruluktan vazgeçilmiyor.
        IReadOnlyList<SideBySideRow> rows = SideBySideDiff.Build(Hunk(
            Removed("aaaa bbbb cccc"),
            Added("aaaa bbbb cccc dddd"),
            Added("tamamen alakasiz bir sey")));

        SideBySideRow paired = rows.Single(r => r.Left is not null && r.Right is not null);

        paired.Left!.Content.ShouldBe("aaaa bbbb cccc");
        paired.Right!.Content.ShouldBe("aaaa bbbb cccc dddd");

        // Eşsiz satır kendi başına, karşısı dolgu.
        rows.Count(r => r.Left is null && r.Right is not null
            && r.Right.Content == "tamamen alakasiz bir sey").ShouldBe(1);
    }

    [Fact]
    public void Hizalama_satir_ici_vurgulamayla_AYNI_eslemeyi_kullanir()
    {
        // Bu iki mekanizma ayrışırsa kullanıcı aynı ekranda iki çelişkili cevap görür:
        // vurgulama bir çifti işaretlerken yan yana yerleşim başka bir çifti gösterir.
        DiffLine[] lines =
        [
            Removed("public void Bir() { }"),
            Removed("public void Iki() { }"),
            Added("public void Iki() { return; }"),
        ];

        IReadOnlyList<DiffLine> annotated = InlineDiff.Annotate(lines);
        IReadOnlyList<SideBySideRow> rows = SideBySideDiff.Build(Hunk(lines));

        // Vurgulama hangi silinen satırı eşledi?
        string highlighted = annotated
            .Single(l => l.Kind == DiffLineKind.Removed && l.Segments.Count > 0)
            .Content;

        // Yan yana yerleşim aynı satırı eşlemeli.
        SideBySideRow paired = rows.Single(r => r.Left is not null && r.Right is not null);

        paired.Left!.Content.ShouldBe(highlighted);
    }

    [Fact]
    public void Sol_sutun_dosyanin_eski_hali_sag_sutun_yeni_hali()
    {
        // Projeksiyon değişmezi — hizalamanın asıl doğruluk ölçütü.
        IReadOnlyList<SideBySideRow> rows = SideBySideDiff.Build(Hunk(
            Context("using System;"),
            Context(""),
            Removed("class Eski"),
            Added("class Yeni"),
            Context("{"),
            Removed("    int a;"),
            Removed("    int b;"),
            Added("    int b;"),
            Context("}")));

        LeftColumn(rows).ShouldBe([
            "using System;", "", "class Eski", "{", "    int a;", "    int b;", "}"]);

        RightColumn(rows).ShouldBe([
            "using System;", "", "class Yeni", "{", "    int b;", "}"]);
    }

    [Fact]
    public void Dosyanin_tum_hunklari_baslikla_ayrilir()
    {
        FileDiff diff = new()
        {
            Path = RepositoryPath.Parse("a.cs"),
            Change = FileChangeKind.Modified,
            Hunks =
            [
                Hunk(Added("bir")),
                Hunk(Removed("iki")),
            ],
        };

        IReadOnlyList<SideBySideRow> rows = SideBySideDiff.Build(diff);

        rows.Count.ShouldBe(4);
        rows[0].IsHunkHeader.ShouldBeTrue();
        rows[2].IsHunkHeader.ShouldBeTrue();

        // Başlık satırının iki tarafı da boş.
        rows[0].Left.ShouldBeNull();
        rows[0].Right.ShouldBeNull();
    }

    [Fact]
    public void Hunksiz_dosyada_satir_uretilmez()
    {
        FileDiff diff = new()
        {
            Path = RepositoryPath.Parse("resim.png"),
            Change = FileChangeKind.Modified,
            Hunks = [],
            IsBinary = true,
        };

        SideBySideDiff.Build(diff).ShouldBeEmpty();
    }
}
