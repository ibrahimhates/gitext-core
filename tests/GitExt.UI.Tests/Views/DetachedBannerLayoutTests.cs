using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;
using GitExt.UI.Views;

namespace GitExt.UI.Tests.Views;

/// <summary>
/// P06-T04 — ayrık HEAD şeridinin gerçekten çizilmesi.
/// </summary>
/// <remarks>
/// ViewModel testi <c>ShowDetachedBanner</c>'ı doğruluyor; burada bağlamanın <b>gerçekten</b>
/// bağlandığı doğrulanıyor. P05'te ölçüldüğü gibi bir bağlama sessizce çalışmayabiliyor.
/// </remarks>
public class DetachedBannerLayoutTests
{
    private static async Task<MainWindow> CreateAsync(bool detached, InProgressOperation operation)
    {
        RepositoryRefs refs = detached
            ? FakeGitData.Refs(
                head: new HeadState
                {
                    IsDetached = true,
                    IsUnborn = false,
                    Commit = CommitId.Parse(FakeGitData.Sha(3)),
                })
            : FakeGitData.Refs(
                localBranches: [FakeGitData.LocalBranch("main", FakeGitData.Sha(3), isCurrent: true)]);

        MainWindowViewModel model = new(
            new CommitListViewModel(
                new FakeRepositoryLocator(),
                new FakeCommitLogReader(FakeGitData.LinearHistory(3)),
                new FakeRefReader(refs),
                new FakeCommitSignatureReader(),
                new FakeDiffReader()),
            new FakeRecentRepositoryStore(),
            branchWriter: new FakeBranchWriter(),
            operations: new FakeInProgressOperationReader(operation));

        MainWindow window = new() { DataContext = model };
        window.Show();

        // ⚠️ `GetAwaiter().GetResult()` burada KİLİTLENİYOR: devamı UI iş parçacığını
        // istiyor, o da bloke oluyor. Test async olmalı.
        await model.OpenRepositoryAsync("/tmp/depo");

        return window;
    }

    [AvaloniaFact]
    public async Task Ayrik_HEAD_te_serit_GERCEKTEN_ciziliyor()
    {
        MainWindow window = await CreateAsync(detached: true, operation: InProgressOperation.None);

        window.GetControl<Border>("DetachedBanner").IsVisible.ShouldBeTrue();
        window.GetControl<Border>("OperationBanner").IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Serit_BURADAN_DAL_OLUSTUR_eylemi_sunuyor()
    {
        // Plan bu görevde açıkça bunu istiyor: uyarı tek başına yetmez, çıkış yolu lazım.
        MainWindow window = await CreateAsync(detached: true, operation: InProgressOperation.None);

        window.GetControl<Button>("DetachedCreateBranchButton").IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Rebase_sirasinda_ISLEM_seridi_ciziliyor_ayrik_serit_DEGIL()
    {
        MainWindow window = await CreateAsync(detached: true, operation: InProgressOperation.Rebase);

        window.GetControl<Border>("DetachedBanner").IsVisible.ShouldBeFalse();
        window.GetControl<Border>("OperationBanner").IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task Normal_dalda_iki_serit_de_GIZLI()
    {
        MainWindow window = await CreateAsync(detached: false, operation: InProgressOperation.None);

        window.GetControl<Border>("DetachedBanner").IsVisible.ShouldBeFalse();
        window.GetControl<Border>("OperationBanner").IsVisible.ShouldBeFalse();
    }
}
