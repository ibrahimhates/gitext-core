using Avalonia.Headless.XUnit;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P09-T10 · P09-T14 — memory growth and the hunt for leaks.
/// </summary>
/// <remarks>
/// <para>
/// The budget's items: <i>"Memory use must <b>not</b> grow with scrolling"</i> and
/// <i>"verify that the scope really is disposed when the repository is closed"</i> (ADR-0004).
/// </para>
/// <para>
/// The measurement is made with a <b>weak reference</b>, not with a memory figure:
/// <c>GC.GetTotalMemory</c> is noisy and any "how many MB counts as a leak" threshold would be
/// arbitrary. A weak reference surviving, on the other hand, is a definite fact — if the object was
/// not collected, somebody is still holding it.
/// </para>
/// </remarks>
public class MemoryStressTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CommitListViewModel Create(int commitCount) =>
        new(new FakeRepositoryLocator(),
            new FakeCommitLogReader(FakeGitData.LinearHistory(commitCount)),
            new FakeRefReader(),
            new FakeCommitSignatureReader(),
            new FakeDiffReader());

    /// <summary>
    /// Forces a collection and says whether the object is still alive.
    /// </summary>
    /// <remarks>
    /// Two rounds: objects with a finaliser only enter the queue on the first round and are actually
    /// collected on the second. A single round would show a non-live object as "leaked" and produce a
    /// false alarm.
    /// </remarks>
    private static bool StillAlive(WeakReference reference)
    {
        for (int i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return reference.IsAlive;
    }

    /// <remarks>
    /// 🔴 The real risk: even with the rows cleared, if the <c>_rowIndex</c> dictionary keeps holding
    /// the commit ids, the closed repository's data stays in memory. Over a long session moving
    /// between repositories, that is a leak accumulating with every switch.
    /// </remarks>
    [AvaloniaFact]
    public async Task Kapatma_satirlari_gercekten_birakiyor()
    {
        CommitListViewModel model = Create(50);

        await model.OpenAsync("/depo", Ct).ConfigureAwait(true);
        model.Rows.ShouldNotBeEmpty("fixture hiç satır üretmedi — test bir şey ölçmüyor");

        WeakReference weak = Forget(model);

        model.Close();

        StillAlive(weak).ShouldBeFalse("kapatılan deponun satırı hâlâ tutuluyor");

        GC.KeepAlive(model);
    }

    /// <remarks>
    /// The reference is left in a separate method: a local variable in the same method could keep the
    /// object alive and the test would measure nothing.
    /// </remarks>
    /// <summary>
    /// Produces a weak reference to the first row and releases the strong one.
    /// </summary>
    /// <remarks>
    /// ⚠️ It must be a separate and <b>synchronous</b> method. Held in a local of the calling
    /// <c>async</c> test, the row becomes a field in the compiler-generated state machine and stays
    /// alive until the test finishes; the measurement would then measure the state machine's lifetime
    /// rather than whether closing works.
    /// </remarks>
    private static WeakReference Forget(CommitListViewModel model) => new(model.Rows[0]);

    /// <remarks>
    /// Rapid switching between repositories (P09-T14). Every open must release the previous one;
    /// without that, memory grows with each switch and swells over a session left open for hours.
    /// </remarks>
    [AvaloniaFact]
    public async Task Depolar_arasi_gecis_oncekini_birakiyor()
    {
        CommitListViewModel model = Create(30);
        List<WeakReference> generations = [];

        for (int repository = 0; repository < 5; repository++)
        {
            await model.OpenAsync("/depo", Ct).ConfigureAwait(true);
            generations.Add(Forget(model));
        }

        foreach (WeakReference weak in generations.Take(generations.Count - 1))
        {
            StillAlive(weak).ShouldBeFalse("önceki deponun satırları tutuluyor");
        }

        GC.KeepAlive(model);
    }

    /// <remarks>
    /// 🔴 <b>Closing must clear the details panel too.</b> <c>Show(null, …)</c> used to turn off only
    /// <c>HasCommit</c>, while <c>Badges</c> and <c>Parents</c> kept holding objects belonging to the
    /// closed repository. Because the panel was hidden it could not be seen by eye; badges belonging
    /// to a closed repository were sitting in memory.
    /// <para>
    /// This is a case the weak-reference test <b>cannot</b> catch: the row itself is released from
    /// elsewhere anyway. What is measurable is that the panel is explicitly left empty.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task Kapatma_detay_panelini_de_temizliyor()
    {
        CommitListViewModel model = Create(20);

        await model.OpenAsync("/depo", Ct).ConfigureAwait(true);
        model.SelectedIndex = 0;

        model.Details.HasCommit.ShouldBeTrue("fixture seçim yapmadı — test bir şey ölçmüyor");

        model.Close();

        model.Details.HasCommit.ShouldBeFalse();
        model.Details.Badges.ShouldBeEmpty("kapatılan deponun rozetleri tutuluyor");
        model.Details.Parents.ShouldBeEmpty("kapatılan deponun ebeveyn bağlantıları tutuluyor");
        model.Details.FullId.ShouldBeEmpty("kapatılan deponun commit kimliği duruyor");
        model.Details.Subject.ShouldBeEmpty();
    }

    /// <remarks>
    /// Closing must reset the counters as well; without that the UI carries on showing "N commits
    /// loaded" for a closed repository — the data is gone, the number remains.
    /// </remarks>
    [AvaloniaFact]
    public async Task Kapatma_sayaclari_sifirliyor()
    {
        CommitListViewModel model = Create(20);

        await model.OpenAsync("/depo", Ct).ConfigureAwait(true);
        model.LoadedCount.ShouldBeGreaterThan(0);

        model.Close();

        model.Rows.ShouldBeEmpty();
        model.LoadedCount.ShouldBe(0);
        model.SelectedIndex.ShouldBe(-1);
        model.Repository.ShouldBeNull();
    }

    /// <remarks>
    /// Opening the same repository over and over must <b>not</b> increase the row count. If it does,
    /// opening is not clearing the previous one, and in a session that refreshes, the list would grow
    /// exponentially.
    /// </remarks>
    [AvaloniaFact]
    public async Task Tekrarli_acilis_satirlari_biriktirmiyor()
    {
        CommitListViewModel model = Create(25);

        await model.OpenAsync("/depo", Ct).ConfigureAwait(true);
        int first = model.Rows.Count;

        for (int i = 0; i < 4; i++)
        {
            await model.OpenAsync("/depo", Ct).ConfigureAwait(true);
        }

        model.Rows.Count.ShouldBe(first);
    }
}
