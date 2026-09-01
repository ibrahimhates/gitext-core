using GitExt.Core.Git;

namespace GitExt.Core.Tests.Git;

/// <summary>
/// P14-T01 — the screen buffer behind the process window.
/// </summary>
/// <remarks>
/// <see cref="GitOutputText"/> cleans a <b>finished</b> command's text; this one is fed while the
/// process is still writing, so it has to behave like a terminal rather than like a cleaner.
/// </remarks>
public class GitConsoleBufferTests
{
    private const string Escape = "\u001b";

    [Fact]
    public void Bos_tampon_bos_metin_verir()
    {
        GitConsoleBuffer buffer = new();

        buffer.GetText().ShouldBe(string.Empty);
        buffer.LastLine.ShouldBe(string.Empty);
        buffer.EndsWithNewLine.ShouldBeTrue();
    }

    [Fact]
    public void Parcalar_birlestirilir()
    {
        GitConsoleBuffer buffer = new();

        buffer.Append("remote: Enume");
        buffer.Append("rating objects: 12\n");

        buffer.GetText().ShouldBe("remote: Enumerating objects: 12");
    }

    [Fact]
    public void Satir_basi_satiri_bitirmez_ustune_yazar()
    {
        GitConsoleBuffer buffer = new();

        buffer.Append("Receiving objects:  10% (1/10)\r");
        buffer.Append("Receiving objects: 100% (10/10)\r");

        buffer.GetText().ShouldBe("Receiving objects: 100% (10/10)");
        buffer.GetText().ShouldNotContain("10%");
    }

    [Fact]
    public void Kisa_satir_uzun_satirin_kuyrugunu_birakmaz()
    {
        GitConsoleBuffer buffer = new();

        buffer.Append("Counting objects: 100% (1234/1234), done.\r");
        buffer.Append($"Done.{Escape}[K");

        buffer.GetText().ShouldBe("Done.");
    }

    [Fact]
    public void Silme_dizisi_yoksa_kuyruk_gorunur_kalir()
    {
        GitConsoleBuffer buffer = new();

        buffer.Append("uzun bir ilerleme satiri\r");
        buffer.Append("kisa");

        buffer.GetText().ShouldBe("kisa bir ilerleme satiri");
    }

    [Fact]
    public void Satir_sonu_yeni_satir_acar()
    {
        GitConsoleBuffer buffer = new();

        buffer.Append("bir\niki\n");

        buffer.GetText().ShouldBe("bir\niki");
        buffer.LastLine.ShouldBe("iki");
    }

    [Fact]
    public void Yarim_satir_beklendigini_bildirir()
    {
        GitConsoleBuffer buffer = new();

        buffer.Append("tamam\n");
        buffer.EndsWithNewLine.ShouldBeTrue();

        buffer.Append("Username for 'https://github.com': ");
        buffer.EndsWithNewLine.ShouldBeFalse();
        buffer.LastLine.ShouldBe("Username for 'https://github.com':");
    }

    [Fact]
    public void ANSI_renk_kodlari_gorunmez()
    {
        GitConsoleBuffer buffer = new();

        buffer.Append($"{Escape}[31mKIRMIZI HATA{Escape}[0m\n");

        buffer.GetText().ShouldBe("KIRMIZI HATA");
    }

    [Fact]
    public void Sekme_sekiz_sutuna_hizalanir()
    {
        GitConsoleBuffer buffer = new();

        buffer.Append("a\tb\n");

        buffer.GetText().ShouldBe("a       b");
    }

    [Fact]
    public void Satir_siniri_asilinca_bastan_dusurulur()
    {
        GitConsoleBuffer buffer = new(maximumLines: 10);

        for (int index = 0; index < 5000; index++)
        {
            buffer.Append($"satir {index}\n");
        }

        string[] lines = buffer.GetText().Split('\n');

        lines.Length.ShouldBeLessThanOrEqualTo(10 + 256);
        buffer.DroppedLines.ShouldBeGreaterThan(0);
        buffer.GetText().ShouldContain("satir 4999");
        buffer.GetText().ShouldNotContain("satir 0\n");
    }

    [Fact]
    public void Temizlemek_her_seyi_sifirlar()
    {
        GitConsoleBuffer buffer = new(maximumLines: 2);

        buffer.Append("bir\niki\nuc\ndort\n");
        buffer.Clear();

        buffer.GetText().ShouldBe(string.Empty);
        buffer.LastLine.ShouldBe(string.Empty);
        buffer.DroppedLines.ShouldBe(0);
        buffer.EndsWithNewLine.ShouldBeTrue();
    }

    [Fact]
    public void Gercek_klon_ciktisi_tek_ilerleme_satirina_iner()
    {
        GitConsoleBuffer buffer = new();

        buffer.Append("Cloning into 'x'...\n");

        for (int percent = 0; percent <= 100; percent += 10)
        {
            buffer.Append($"Receiving objects: {percent,3}% ({percent}/100)\r");
        }

        buffer.Append("\nResolving deltas: 100% (5/5), done.\n");

        string[] lines = buffer.GetText().Split('\n');

        lines.Length.ShouldBe(3);
        lines[0].ShouldBe("Cloning into 'x'...");
        lines[1].ShouldBe("Receiving objects: 100% (100/100)");
        lines[2].ShouldBe("Resolving deltas: 100% (5/5), done.");
    }
}
