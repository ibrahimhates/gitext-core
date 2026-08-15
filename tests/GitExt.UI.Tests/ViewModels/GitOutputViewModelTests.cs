using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T07 — hook/git çıktısının gösterime hazırlanması.
/// </summary>
public class GitOutputViewModelTests
{
    private static GitException Failure(string standardError, int exitCode = 1) =>
        new(
            GitFailureKind.Unknown,
            "Git komutu başarısız oldu.",
            "git commit -F -",
            exitCode,
            standardError);

    private static CommitResult Result(
        string output = "",
        string message = "konu",
        string requested = "konu") =>
        new()
        {
            Id = CommitId.Parse(new string('a', 40)),
            Message = message,
            RequestedMessage = requested,
            Output = output,
        };

    [Fact]
    public void Basarisiz_komutta_cikis_kodu_ve_tam_cikti_tasinir()
    {
        GitOutputViewModel view = GitOutputViewModel.ForFailure(
            Failure("pre-commit: 3 dosyada bicim hatasi\nsrc/a.cs:12"));

        view.ExitCode.ShouldBe(1);
        view.HasExitCode.ShouldBeTrue();
        view.ExitCodeText.ShouldBe("Çıkış kodu: 1");
        view.CommandLine.ShouldBe("git commit -F -");
        view.Output.ShouldContain("pre-commit: 3 dosyada bicim hatasi");
        view.Output.ShouldContain("src/a.cs:12");
        view.HasOutput.ShouldBeTrue();
    }

    [Fact]
    public void Ciktidaki_ANSI_kodlari_gosterilmez()
    {
        GitOutputViewModel view = GitOutputViewModel.ForFailure(
            Failure("\u001b[31mhata\u001b[0m"));

        view.Output.ShouldBe("hata");
    }

    [Fact]
    public void Basarili_committe_hook_ciktisi_gosterilir()
    {
        GitOutputViewModel view = GitOutputViewModel.ForCommit(
            Result(output: "UYARI: iki TODO satiri var"));

        view.HasOutput.ShouldBeTrue();
        view.Output.ShouldContain("UYARI: iki TODO satiri var");

        // Başarı yolunda çıkış kodu YOK: 0 göstermek "bir sorun var mı?" sorusu doğururdu.
        view.HasExitCode.ShouldBeFalse();
    }

    [Fact]
    public void Gosterilecek_sey_yoksa_bunu_SONUC_soyler()
    {
        // Karar görünümde değil sonuçta: ayrı pencerede boş içerik gürültüdür ama commit
        // paneline gömülü bir bölümde (P05-T12) "çıktı yok" gayet iyi bir cevaptır.
        Result().NeedsReporting.ShouldBeFalse();
        Result(output: "cikti").NeedsReporting.ShouldBeTrue();
        Result(message: "konu\n\nChange-Id: I1").NeedsReporting.ShouldBeTrue();
    }

    [Fact]
    public void Mesaj_degistiyse_kaydedilen_mesaj_gosterilir()
    {
        GitOutputViewModel view = GitOutputViewModel.ForCommit(
            Result(message: "konu\n\nChange-Id: I1", requested: "konu"));

        view.HasFinalMessage.ShouldBeTrue();
        view.FinalMessage.ShouldContain("Change-Id: I1");
        view.Summary.ShouldContain("changed the commit message");
    }

    [Fact]
    public void Mesaj_degismediyse_mesaj_bolumu_gosterilmez()
    {
        GitOutputViewModel view = GitOutputViewModel.ForCommit(
            Result(output: "cikti", message: "konu"));

        view.HasFinalMessage.ShouldBeFalse();
    }

    [Fact]
    public void Cok_uzun_ciktida_kirpma_notu_gosterilir()
    {
        string output = string.Join(
            '\n',
            Enumerable.Range(0, GitOutputText.MaximumDisplayLines + 42).Select(i => $"satir {i}"));

        GitOutputViewModel view = GitOutputViewModel.ForFailure(Failure(output));

        view.HasTruncationNotice.ShouldBeTrue();
        view.TruncationNotice.ShouldContain("42");
        view.Output.Split('\n').Length.ShouldBe(GitOutputText.MaximumDisplayLines);
    }
}
