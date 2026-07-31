using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P05-T12 — commit paneli: mesaj kutusu, 50/72 kılavuzu, sayaç, <c>Ctrl+Enter</c>.
/// </summary>
public class CommitPanelTests
{
    private static FileStatus Staged(string path) =>
        new() { Path = RepositoryPath.Parse(path), StagedChange = FileChangeKind.Modified };

    private static FileStatus Unstaged(string path) =>
        new() { Path = RepositoryPath.Parse(path), UnstagedChange = FileChangeKind.Modified };

    private sealed record Harness(
        WorkingTreeViewModel Model,
        FakeStatusReader Status,
        FakeCommitWriter Commits);

    private static async Task<Harness> CreateAsync(params FileStatus[] entries)
    {
        FakeStatusReader status = new(entries);
        FakeCommitWriter commits = new(status);

        WorkingTreeViewModel model = new(
            status,
            new FakeStagingWriter(status),
            commits,
            new DiffViewModel(new FakeDiffReader()));

        await model.OpenAsync("/tmp/depo");

        return new Harness(model, status, commits);
    }

    // ---- Mesaj kutusu ----

    [AvaloniaFact]
    public void Konu_sayaci_ilk_satiri_sayar()
    {
        CommitMessageViewModel message = new() { Text = "konu satiri\n\ngovde cok daha uzun olabilir" };

        message.SubjectLength.ShouldBe("konu satiri".Length);
        message.SubjectCounter.ShouldBe($"11 / {CommitMessageViewModel.SubjectLimit}");
        message.IsSubjectTooLong.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Uzun_konu_isaretlenir_ama_ENGELLENMEZ()
    {
        // Uzun konu bir tercih olabilir; uygulamanın kullanıcıyı kendi deposunda kısıtlaması
        // doğru değil. GitExtensions'ta da bu bir onay diyaloğu, engel değil.
        CommitMessageViewModel message = new() { Text = new string('x', 60) };

        message.IsSubjectTooLong.ShouldBeTrue();
        message.Hint.ShouldContain("50");
        message.IsEmpty.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Ikinci_satirin_dolu_olmasi_uyarilir()
    {
        // git bu ayrımı anlamlı sayıyor: `%s` ilk satır, `%b` boş satırdan sonrası.
        CommitMessageViewModel message = new() { Text = "konu\ngovde\nbiraz daha" };

        message.HasNonEmptySecondLine.ShouldBeTrue();
        message.Hint.ShouldContain("boş satır");
    }

    [AvaloniaFact]
    public void Duzgun_mesajda_uyari_YOK()
    {
        CommitMessageViewModel message = new() { Text = "konu\n\ngovde" };

        message.HasHint.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void CRLF_satir_sonlari_konu_uzunlugunu_bozmaz()
    {
        CommitMessageViewModel message = new() { Text = "konu\r\n\r\ngovde" };

        message.SubjectLength.ShouldBe(4);
        message.HasNonEmptySecondLine.ShouldBeFalse();
    }

    // ---- Commit akışı ----

    [AvaloniaFact]
    public async Task Mesaj_bosken_commit_KAPALI()
    {
        // Boş mesajla `git commit` çıkış 1 veriyor (P05-T06'da ölçüldü); reddedileceği
        // belli olan bir işlemi açık sunmak olurdu.
        Harness harness = await CreateAsync(Staged("a.txt"));

        harness.Model.CanCommit.ShouldBeFalse();

        harness.Model.Message.Text = "konu";
        harness.Model.CanCommit.ShouldBeTrue();

        harness.Model.Message.Text = "   ";
        harness.Model.CanCommit.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Stage_lenmis_degisiklik_yoksa_commit_KAPALI()
    {
        Harness harness = await CreateAsync(Unstaged("a.txt"));

        harness.Model.Message.Text = "konu";

        harness.Model.CanCommit.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Amend_stage_lenmis_degisiklik_olmadan_da_commit_ettirir()
    {
        // `--amend` yalnızca mesajı değiştirmek için de kullanılıyor.
        Harness harness = await CreateAsync();

        harness.Model.Message.Text = "duzeltilmis mesaj";
        harness.Model.CanCommit.ShouldBeFalse();

        harness.Model.Amend = true;
        harness.Model.CanCommit.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Commit_mesaji_ve_amend_bayragi_cekirdege_gecer()
    {
        Harness harness = await CreateAsync(Staged("a.txt"));

        harness.Model.Message.Text = "konu\n\ngovde";
        harness.Model.Amend = true;

        await harness.Model.CommitAsync();

        harness.Commits.Messages.ShouldBe(["konu\n\ngovde"]);
        harness.Commits.Options.Single().Amend.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Basarili_commit_sonrasi_mesaj_TEMIZLENIR()
    {
        // Aynı metinle ikinci bir commit atmak neredeyse her zaman kazadır.
        Harness harness = await CreateAsync(Staged("a.txt"));

        harness.Model.Message.Text = "konu";
        harness.Model.Amend = true;

        await harness.Model.CommitAsync();

        harness.Model.Message.IsEmpty.ShouldBeTrue();
        harness.Model.Amend.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Hook_ciktisi_varsa_commit_sonrasi_GOSTERILIR()
    {
        Harness harness = await CreateAsync(Staged("a.txt"));

        harness.Commits.Output = "UYARI: iki TODO satiri var";
        harness.Model.Message.Text = "konu";

        await harness.Model.CommitAsync();

        harness.Model.CommitOutput.ShouldNotBeNull().Output.ShouldContain("TODO");
    }

    [AvaloniaFact]
    public async Task Gosterilecek_sey_yoksa_pencere_ACILMAZ()
    {
        Harness harness = await CreateAsync(Staged("a.txt"));

        harness.Model.Message.Text = "konu";

        await harness.Model.CommitAsync();

        harness.Model.CommitOutput.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Commit_basarisiz_olursa_mesaj_KORUNUR()
    {
        // 🔑 Mesajı silmek, kullanıcının yazdığı metni hata anında kaybettirmek olurdu.
        Harness harness = await CreateAsync(Staged("a.txt"));

        harness.Commits.Failure = new GitExt.Core.Git.GitException(
            GitExt.Core.Git.GitFailureKind.Unknown,
            "Git komutu başarısız oldu.",
            "git commit -F -",
            exitCode: 1,
            standardError: "pre-commit: reddedildi");

        harness.Model.Message.Text = "kaybolmamali";

        await harness.Model.CommitAsync();

        harness.Model.Message.Text.ShouldBe("kaybolmamali");
        harness.Model.ErrorDetails.ShouldNotBeNull().Output.ShouldContain("reddedildi");
    }

    // ---- Klavye ----

    [AvaloniaFact]
    public async Task Mesaj_kutusunda_BOSLUK_dosya_stage_LEMEZ()
    {
        // 🔴 Tünellenen kısayol yüzünden `Space` mesaja boşluk yazmak yerine dosya
        // stage'liyordu: mesaj yazan kullanıcı farkında olmadan index'i değiştirirdi.
        Harness harness = await CreateAsync(Unstaged("a.txt"));

        WorkingTreeView view = new() { DataContext = harness.Model };
        Window window = new() { Width = 900, Height = 600, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        view.GetControl<TextBox>("MessageBox").Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        harness.Model.Staged.ShouldBeEmpty();
        harness.Model.Unstaged.Count.ShouldBe(1);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Ctrl_Enter_commit_atar_ve_mesaja_SATIR_SONU_eklemez()
    {
        // ÖLÇÜLDÜ: `AcceptsReturn` açık bir `TextBox` Ctrl+Enter'ı da satır sonu olarak
        // işliyor; yakalanıp `Handled` işaretlenmezse mesaja boş satır girerdi.
        Harness harness = await CreateAsync(Staged("a.txt"));

        harness.Model.Message.Text = "konu";

        WorkingTreeView view = new() { DataContext = harness.Model };
        Window window = new() { Width = 900, Height = 600, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        view.GetControl<TextBox>("MessageBox").Focus();
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        harness.Commits.Messages.ShouldBe(["konu"]);

        window.Close();
    }
}
