using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P12-T07 — the filter toolbar's state turning into a git query.
/// </summary>
/// <remarks>
/// What is asserted is <b>the query handed to git</b>. The filtering itself is git's job (its
/// behaviour is pinned down against a real repository in <c>CommitLogFilterTests</c>); hiding rows
/// we had already read would mean paying the full cost of reading them for nothing.
/// </remarks>
public class CommitFilterTests
{
    private static (CommitListViewModel List, FakeCommitLogReader Reader) Create()
    {
        FakeCommitLogReader reader = new(FakeGitData.LinearHistory(3));

        CommitListViewModel list = new(
            new FakeRepositoryLocator(),
            reader,
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

        return (list, reader);
    }

    private static async Task<(CommitListViewModel List, FakeCommitLogReader Reader)> OpenAsync()
    {
        (CommitListViewModel list, FakeCommitLogReader reader) = Create();
        await list.OpenAsync("/tmp/depo");
        return (list, reader);
    }

    [AvaloniaFact]
    public async Task Varsayilan_sorgu_TUM_dallari_okuyor()
    {
        (_, FakeCommitLogReader reader) = await OpenAsync();

        CommitLogQuery query = reader.LastQuery.ShouldNotBeNull();

        query.IncludeAllRefs.ShouldBeTrue();
        query.MessageContains.ShouldBeNull();
        query.FirstParentOnly.ShouldBeFalse();
    }

    [AvaloniaTheory]
    [InlineData(RevisionFilterKind.Message)]
    [InlineData(RevisionFilterKind.Committer)]
    [InlineData(RevisionFilterKind.Author)]
    [InlineData(RevisionFilterKind.DiffContains)]
    public async Task Filtre_turu_metni_DOGRU_alana_koyuyor(RevisionFilterKind kind)
    {
        // 🔴 The mapping is the whole of it: putting the text in the wrong field means git
        // answering a different question — and an answer to a different question still looks
        // like a list of commits.
        (CommitListViewModel list, FakeCommitLogReader reader) = await OpenAsync();

        list.FilterText = "ada";
        await list.SetFilterKindCommand.ExecuteAsync(kind);

        CommitLogQuery query = reader.LastQuery.ShouldNotBeNull();

        query.MessageContains.ShouldBe(kind == RevisionFilterKind.Message ? "ada" : null);
        query.Committer.ShouldBe(kind == RevisionFilterKind.Committer ? "ada" : null);
        query.Author.ShouldBe(kind == RevisionFilterKind.Author ? "ada" : null);
        query.DiffContains.ShouldBe(kind == RevisionFilterKind.DiffContains ? "ada" : null);
    }

    [AvaloniaFact]
    public async Task Bos_filtre_metni_sorguya_GIRMIYOR()
    {
        // A blank box is not a filter matching the empty string; it is no filter at all.
        (CommitListViewModel list, FakeCommitLogReader reader) = await OpenAsync();

        list.FilterText = "   ";
        await list.ApplyFilterCommand.ExecuteAsync(null);

        reader.LastQuery.ShouldNotBeNull().MessageContains.ShouldBeNull();
        list.HasActiveFilter.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Yalnizca_bulunulan_dal_kipi_tum_referanslari_KAPATIYOR()
    {
        (CommitListViewModel list, FakeCommitLogReader reader) = await OpenAsync();

        await list.SetBranchModeCommand.ExecuteAsync(BranchFilterMode.CurrentBranch);

        CommitLogQuery query = reader.LastQuery.ShouldNotBeNull();

        query.IncludeAllRefs.ShouldBeFalse();
        query.Revision.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Filtrelenmis_dal_kipinde_secilen_dal_okunuyor()
    {
        (CommitListViewModel list, FakeCommitLogReader reader) = await OpenAsync();

        list.BranchFilter = "feature/login";
        await list.SetBranchModeCommand.ExecuteAsync(BranchFilterMode.FilteredBranches);

        CommitLogQuery query = reader.LastQuery.ShouldNotBeNull();

        query.Revision.ShouldBe("feature/login");
        query.IncludeAllRefs.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Ilk_ebeveyn_kutusu_TEK_okuma_yapiyor()
    {
        // 🔴 The checkbox binds to the property and the re-read hangs off the property change.
        // The first version ALSO had a command on the checkbox, so a click toggled the value
        // twice and appeared to do nothing at all.
        (CommitListViewModel list, FakeCommitLogReader reader) = await OpenAsync();

        int before = reader.StreamCallCount;

        list.FirstParentOnly = true;
        await Task.Delay(50);

        list.FirstParentOnly.ShouldBeTrue();
        reader.LastQuery.ShouldNotBeNull().FirstParentOnly.ShouldBeTrue();
        (reader.StreamCallCount - before).ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Sifirlama_HER_SEYI_temizliyor_ve_TEK_okuma_yapiyor()
    {
        // 🔴 Five cleared fields must not mean five `git log` processes, four of which are
        // cancelled a moment later. Resetting is one action; it costs one read.
        (CommitListViewModel list, FakeCommitLogReader reader) = await OpenAsync();

        list.FilterText = "ada";
        list.FilterKind = RevisionFilterKind.Author;
        list.BranchMode = BranchFilterMode.CurrentBranch;
        list.FirstParentOnly = true;

        await Task.Delay(50);
        int before = reader.StreamCallCount;

        await list.ResetFiltersCommand.ExecuteAsync(null);

        list.HasActiveFilter.ShouldBeFalse();
        list.FilterKind.ShouldBe(RevisionFilterKind.Message);

        CommitLogQuery query = reader.LastQuery.ShouldNotBeNull();
        query.IncludeAllRefs.ShouldBeTrue();
        query.Author.ShouldBeNull();
        query.FirstParentOnly.ShouldBeFalse();

        (reader.StreamCallCount - before).ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task Etkin_filtre_KULLANICIYA_bildiriliyor()
    {
        // A filter left on and forgotten is the classic way to conclude that a commit "is gone".
        (CommitListViewModel list, _) = await OpenAsync();

        list.HasActiveFilter.ShouldBeFalse();

        list.FilterText = "token";
        list.HasActiveFilter.ShouldBeTrue();

        list.FilterText = null;
        list.BranchMode = BranchFilterMode.CurrentBranch;
        list.HasActiveFilter.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Depo_yokken_filtre_uygulamak_COKMUYOR()
    {
        (CommitListViewModel list, FakeCommitLogReader reader) = Create();

        list.FilterText = "token";
        await list.ApplyFilterCommand.ExecuteAsync(null);

        reader.StreamCallCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Dugme_etiketleri_secimi_yansitiyor()
    {
        (CommitListViewModel list, _) = await OpenAsync();

        list.FilterKindLabel.ShouldBe("Commit message");
        list.BranchModeLabel.ShouldBe("All branches");

        list.FilterKind = RevisionFilterKind.DiffContains;
        list.BranchMode = BranchFilterMode.CurrentBranch;

        list.FilterKindLabel.ShouldBe("Diff contains (SLOW)");
        list.BranchModeLabel.ShouldBe("Current branch only");
    }
}
