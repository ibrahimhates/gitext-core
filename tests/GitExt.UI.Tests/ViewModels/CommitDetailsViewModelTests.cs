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

        // The FULL SHA, not the abbreviated one: the user will copy it and use it elsewhere.
        details.FullId.ShouldBe(FakeGitData.Sha(7));
        details.FullId.Length.ShouldBe(40);
    }

    [AvaloniaFact]
    public void Yazarin_orijinal_saat_dilimi_yerelden_farkliysa_gosterilir()
    {
        CommitDetailsViewModel details = Create();

        details.Show(Row(Commit()), "/tmp/depo");

        details.AuthorDate.ShouldNotBeNullOrWhiteSpace();

        // The machine running the test may be at +09:00; in that case the original date is hidden
        // (there is no point writing the same value twice). We verify both cases.
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
        // They diverge on a rebase/cherry-pick/patch, and that is important information.
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
        // The difference between "unsigned" and "could not be verified" comes from a measured git trap
        // (without an allowedSignersFile, git says "N" to a signed commit). If the panel conflates the
        // two, the user takes a signed commit for an unsigned one.
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
        // The user can hold the ↓ key down. To avoid starting a git process for every row the read is
        // delayed; it has to be cancelled when the selection changes, otherwise the old commit's
        // signature shows up next to the new commit.
        FakeCommitSignatureReader reader = new(new CommitSignatureInfo { Status = SignatureStatus.Valid });
        CommitDetailsViewModel details = new(reader, _ => true);

        for (int i = 0; i < 20; i++)
        {
            details.Show(Row(Commit()), "/tmp/depo");
        }

        await WaitForSignatureAsync(details);

        // The meaningful property: 20 rapid selections must produce a single read. (Saying "there are 0
        // reads right now" would depend on timing and be fragile under load.)
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
