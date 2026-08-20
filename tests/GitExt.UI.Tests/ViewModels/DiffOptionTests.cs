using Avalonia.Headless.XUnit;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P12-T20 — the diff options that change what git produces.
/// </summary>
/// <remarks>
/// <para>
/// These are a different kind of setting from tab width or "show whitespace": those change how
/// the text already in hand is drawn, these change <b>what git is asked for</b> — so each one has
/// to re-read the diff.
/// </para>
/// <para>
/// git's own behaviour is pinned down against a real repository in <c>DiffOptionsTests</c>; what
/// is asserted here is the query the view hands it.
/// </para>
/// </remarks>
public class DiffOptionTests
{
    private static async Task<(DiffViewModel Model, FakeDiffReader Reader)> ShowAsync()
    {
        FakeDiffReader reader = new([Diff("a.txt")]);
        DiffViewModel model = new(reader);

        await model.ShowCommitAsync("/tmp/depo", CommitId.Parse(FakeGitData.Sha(1)));

        return (model, reader);

        static FileDiff Diff(string path) => new()
        {
            Path = RepositoryPath.Parse(path),
            Change = FileChangeKind.Modified,
            Hunks = [],
        };
    }

    [AvaloniaFact]
    public async Task Varsayilan_sorguda_bosluk_yoksayilmiyor()
    {
        (_, FakeDiffReader reader) = await ShowAsync();

        DiffOptions options = reader.LastOptions.ShouldNotBeNull();

        options.Whitespace.ShouldBe(WhitespaceMode.Include);

        // 3 is git's own default, and it is expressed by NOT passing -U at all.
        options.ContextLines.ShouldBeNull();
    }

    [AvaloniaTheory]
    [InlineData(WhitespaceMode.IgnoreEol)]
    [InlineData(WhitespaceMode.IgnoreChange)]
    [InlineData(WhitespaceMode.IgnoreAll)]
    public async Task Bosluk_seviyesi_sorguya_giriyor(WhitespaceMode mode)
    {
        (DiffViewModel model, FakeDiffReader reader) = await ShowAsync();

        model.SetWhitespaceMode(mode);
        await Task.Delay(260);

        reader.LastOptions.ShouldNotBeNull().Whitespace.ShouldBe(mode);
    }

    [AvaloniaFact]
    public async Task Secenek_degisince_diff_YENIDEN_okunuyor()
    {
        // 🔴 Without the re-read the switch would only take effect the next time another file was
        // clicked — which reads as "the setting does nothing".
        (DiffViewModel model, FakeDiffReader reader) = await ShowAsync();

        int before = reader.ReadCallCount;

        model.SetWhitespaceMode(WhitespaceMode.IgnoreAll);
        await Task.Delay(260);

        reader.ReadCallCount.ShouldBeGreaterThan(before);
    }

    [AvaloniaFact]
    public async Task Baglam_satirlari_artip_azaliyor()
    {
        (DiffViewModel model, FakeDiffReader reader) = await ShowAsync();

        model.IncreaseContextLines();
        await Task.Delay(260);
        model.ContextLines.ShouldBe(4);
        reader.LastOptions.ShouldNotBeNull().ContextLines.ShouldBe(4);

        model.DecreaseContextLines();
        model.DecreaseContextLines();
        await Task.Delay(260);
        model.ContextLines.ShouldBe(2);

        // -U0 is a legitimate view (only the changed lines); it does not go below zero.
        for (int i = 0; i < 5; i++)
        {
            model.DecreaseContextLines();
        }

        model.ContextLines.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Tum_dosyayi_goster_ve_geri_don()
    {
        // "Show entire file" is not a mode of its own in git either: it is a very large -U.
        (DiffViewModel model, FakeDiffReader reader) = await ShowAsync();

        model.ToggleEntireFile();
        await Task.Delay(260);

        model.ShowEntireFile.ShouldBeTrue();
        reader.LastOptions.ShouldNotBeNull().ContextLines.ShouldNotBeNull().ShouldBeGreaterThan(1000);

        model.ToggleEntireFile();
        await Task.Delay(260);

        model.ShowEntireFile.ShouldBeFalse();
        model.ContextLines.ShouldBe(3);
    }

    [AvaloniaFact]
    public async Task Etiket_secimi_yansitiyor()
    {
        (DiffViewModel model, _) = await ShowAsync();

        model.ContextLinesLabel.ShouldBe("3 context lines");

        model.ToggleEntireFile();
        model.ContextLinesLabel.ShouldBe("Entire file");
    }

    [AvaloniaFact]
    public async Task Ayni_seviyeye_ikinci_tik_yoksaymayi_KAPATIYOR()
    {
        // In the context menu the items are checkboxes, as they are in GitExtensions; clicking the
        // active one has to mean "stop ignoring", or the box would be unticked on screen while the
        // diff carried on ignoring whitespace.
        (DiffViewModel model, _) = await ShowAsync();

        model.SetWhitespaceMode(WhitespaceMode.IgnoreAll);
        model.IsWhitespaceAllIgnored.ShouldBeTrue();

        model.SetWhitespaceMode(WhitespaceMode.Include);
        model.IsWhitespaceIncluded.ShouldBeTrue();
        model.IsWhitespaceAllIgnored.ShouldBeFalse();
    }
}
