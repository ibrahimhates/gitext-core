using Avalonia.Headless.XUnit;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P03-T15 — Commit detay paneli.
/// </summary>
public class CommitDetailsViewModelTests
{
    private static readonly DateTimeOffset _authoredAt =
        new(2021, 3, 4, 5, 6, 7, TimeSpan.FromHours(9));

    private static CommitInfo Commit(
        Signature? author = null,
        Signature? committer = null,
        IReadOnlyList<string>? parents = null,
        string body = "") =>
        new()
        {
            Id = CommitId.Parse(FakeGitData.Sha(7)),
            Parents = [.. (parents ?? []).Select(CommitId.Parse)],
            Author = author ?? new Signature("Ayşe", "ayse@ornek.invalid", _authoredAt),
            Committer = committer ?? author ?? new Signature("Ayşe", "ayse@ornek.invalid", _authoredAt),
            Subject = "başlık",
            Body = body,
        };

    private static CommitRowViewModel Row(CommitInfo commit, IReadOnlyList<RefBadge>? badges = null) =>
        new(
            commit,
            new GitExt.Graph.GraphLayoutEngine().Add(
                new GitExt.Graph.DagCommit(commit.Id.Value, [.. commit.Parents.Select(p => p.Value)])),
            badges ?? []);

    private static CommitDetailsViewModel Create(
        CommitSignatureInfo? signature = null,
        Action<CommitId>? onNavigate = null) =>
        new(
            new FakeCommitSignatureReader(signature),
            id =>
            {
                onNavigate?.Invoke(id);
                return true;
            });

    [AvaloniaFact]
    public void Secim_yokken_panel_bos()
    {
        CommitDetailsViewModel details = Create();

        details.Show(null, "/tmp/depo");

        details.HasCommit.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Tam_sha_gosterilir()
    {
        CommitDetailsViewModel details = Create();

        details.Show(Row(Commit()), "/tmp/depo");

        // Kısaltılmış değil TAM SHA: kullanıcı kopyalayıp başka yerde kullanacak.
        details.FullId.ShouldBe(FakeGitData.Sha(7));
        details.FullId.Length.ShouldBe(40);
    }

    [AvaloniaFact]
    public void Yazarin_orijinal_saat_dilimi_yerelden_farkliysa_gosterilir()
    {
        CommitDetailsViewModel details = Create();

        details.Show(Row(Commit()), "/tmp/depo");

        details.AuthorDate.ShouldNotBeNullOrWhiteSpace();

        // Testin çalıştığı makine +09:00'da olabilir; o durumda orijinal tarih gizlenir
        // (aynı değeri iki kez yazmanın anlamı yok). İki durumu da doğruluyoruz.
        if (TimeZoneInfo.Local.GetUtcOffset(_authoredAt) == TimeSpan.FromHours(9))
        {
            details.AuthorOriginalDate.ShouldBeNull();
        }
        else
        {
            details.AuthorOriginalDate.ShouldNotBeNull();
            details.AuthorOriginalDate!.ShouldContain("+09:00");
        }
    }

    [AvaloniaFact]
    public void Kaydeden_yazarla_ayniysa_ayrica_gosterilmez()
    {
        CommitDetailsViewModel details = Create();

        details.Show(Row(Commit()), "/tmp/depo");

        details.CommitterDiffersFromAuthor.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Kaydeden_yazardan_farkliysa_isaretlenir()
    {
        // Rebase/cherry-pick/yama durumunda ayrışır ve bu önemli bilgidir.
        CommitDetailsViewModel details = Create();

        details.Show(
            Row(Commit(committer: new Signature("Bot", "bot@ornek.invalid", _authoredAt))),
            "/tmp/depo");

        details.CommitterDiffersFromAuthor.ShouldBeTrue();
        details.CommitterText.ShouldContain("Bot");
        details.AuthorText.ShouldContain("Ayşe");
    }

    [AvaloniaFact]
    public void Ebeveynler_tiklanabilir_baglanti_olur()
    {
        List<CommitId> navigated = [];
        CommitDetailsViewModel details = Create(onNavigate: navigated.Add);

        details.Show(Row(Commit(parents: [FakeGitData.Sha(5), FakeGitData.Sha(6)])), "/tmp/depo");

        details.HasParents.ShouldBeTrue();
        details.Parents.Count.ShouldBe(2);

        details.Parents[1].Command.Execute(details.Parents[1].Id);

        navigated.Single().Value.ShouldBe(FakeGitData.Sha(6));
    }

    [AvaloniaFact]
    public void Kok_committe_ebeveyn_bolumu_gizlenir()
    {
        CommitDetailsViewModel details = Create();

        details.Show(Row(Commit()), "/tmp/depo");

        details.HasParents.ShouldBeFalse();
        details.Parents.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task Imza_gecerliyse_guvenilir_isaretlenir()
    {
        CommitDetailsViewModel details = Create(new CommitSignatureInfo
        {
            Status = SignatureStatus.Valid,
            Signer = "ayse@ornek.invalid",
            Key = "SHA256:xxx",
        });

        details.Show(Row(Commit()), "/tmp/depo");

        await WaitForSignatureAsync(details);

        details.SignatureIsTrusted.ShouldBeTrue();
        details.SignatureIsProblem.ShouldBeFalse();
        details.SignatureDetail!.ShouldContain("ayse@ornek.invalid");
    }

    [AvaloniaFact]
    public async Task Dogrulanamayan_imza_sorun_olarak_isaretlenir()
    {
        // "İmzasız" ile "doğrulanamadı" farkı ölçülmüş bir git tuzağından geliyor
        // (allowedSignersFile yoksa git imzalı commit'e "N" diyor). Panel bu ikisini
        // karıştırırsa kullanıcı imzalı bir commit'i imzasız sanır.
        CommitDetailsViewModel details = Create(new CommitSignatureInfo
        {
            Status = SignatureStatus.CannotVerify,
            CannotVerifyReason = "allowedSignersFile yapılandırılmamış",
        });

        details.Show(Row(Commit()), "/tmp/depo");

        await WaitForSignatureAsync(details);

        details.SignatureIsTrusted.ShouldBeFalse();
        details.SignatureIsProblem.ShouldBeTrue();
        details.SignatureText.ShouldNotBeNullOrEmpty();
        details.SignatureDetail!.ShouldContain("allowedSignersFile");
    }

    [AvaloniaFact]
    public async Task Imzasiz_committe_imza_satiri_hic_cikmaz()
    {
        CommitDetailsViewModel details = Create();

        details.Show(Row(Commit()), "/tmp/depo");

        await WaitForSignatureAsync(details, expectSignature: false);

        details.SignatureText.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Hizli_gezinmede_onceki_imza_okumasi_iptal_edilir()
    {
        // Kullanıcı ↓ tuşuna basılı tutabilir. Her satır için git süreci başlatmamak adına
        // okuma gecikmeli; seçim değişince iptal edilmeli, yoksa eski commit'in imzası
        // yeni commit'in yanında görünür.
        FakeCommitSignatureReader reader = new(new CommitSignatureInfo { Status = SignatureStatus.Valid });
        CommitDetailsViewModel details = new(reader, _ => true);

        for (int i = 0; i < 20; i++)
        {
            details.Show(Row(Commit()), "/tmp/depo");
        }

        // Gecikme dolmadan hepsi iptal edildiği için hiç okuma yapılmamalı.
        reader.ReadCallCount.ShouldBe(0);

        await WaitForSignatureAsync(details);

        // Yalnızca son seçim okunur.
        reader.ReadCallCount.ShouldBe(1);
    }

    private static async Task WaitForSignatureAsync(
        CommitDetailsViewModel details,
        bool expectSignature = true)
    {
        for (int i = 0; i < 50 && (details.SignatureText is null) == expectSignature; i++)
        {
            await Task.Delay(20);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }
}
