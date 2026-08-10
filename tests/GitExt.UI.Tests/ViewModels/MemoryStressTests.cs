using Avalonia.Headless.XUnit;
using GitExt.UI.Tests.Fakes;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P09-T10 · P09-T14 — bellek büyümesi ve sızıntı avı.
/// </summary>
/// <remarks>
/// <para>
/// Bütçenin maddeleri: <i>"Bellek kullanımı kaydırmayla <b>artmamalı</b>"</i> ve
/// <i>"repo kapatıldığında scope'un gerçekten temizlendiğini doğrula"</i> (ADR-0004).
/// </para>
/// <para>
/// Ölçüm <b>zayıf referansla</b> yapılıyor, bellek sayısıyla değil:
/// <c>GC.GetTotalMemory</c> gürültülü ve "kaç MB'tan sonra sızıntı sayılır" eşiği keyfî
/// olurdu. Zayıf referansın hayatta kalması ise kesin bir olgu — nesne toplanmadıysa
/// birileri onu hâlâ tutuyor.
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
    /// Toplamayı zorlar ve nesnenin hâlâ yaşayıp yaşamadığını söyler.
    /// </summary>
    /// <remarks>
    /// İki tur: sonlandırıcısı olan nesneler ilk turda yalnızca kuyruğa giriyor, ancak
    /// ikinci turda gerçekten toplanıyorlar. Tek tur, canlı olmayan bir nesneyi "sızmış"
    /// gösterip yanlış alarm üretirdi.
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
    /// 🔴 Asıl risk: satırlar temizlense bile <c>_rowIndex</c> sözlüğü commit
    /// kimliklerini tutmaya devam ederse kapatılan deponun verisi bellekte kalır.
    /// Depolar arasında geçen uzun bir oturumda bu, her geçişte biriken bir sızıntı olur.
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
    /// Referans ayrı bir metotta bırakılıyor: aynı metottaki bir yerel değişken nesneyi
    /// canlı tutabilir ve test hiçbir şey ölçmezdi.
    /// </remarks>
    /// <summary>
    /// İlk satıra zayıf bir referans üretir ve güçlü referansı bırakır.
    /// </summary>
    /// <remarks>
    /// ⚠️ Ayrı ve <b>senkron</b> bir metot olması şart. Satır, çağıran <c>async</c> testin
    /// yerel değişkeninde tutulursa derleyicinin ürettiği durum makinesinde alan oluyor ve
    /// test bitene kadar canlı kalıyor; ölçüm o hâliyle kapatmanın işe yarayıp
    /// yaramadığını değil, durum makinesinin ömrünü ölçerdi.
    /// </remarks>
    private static WeakReference Forget(CommitListViewModel model) => new(model.Rows[0]);

    /// <remarks>
    /// Depolar arası hızlı geçiş (P09-T14). Her açılış öncekini bırakmalı; bırakmazsa
    /// bellek her geçişte artar ve saatlerce açık kalan oturumda şişer.
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
    /// 🔴 <b>Kapatma detay panelini de temizlemeli.</b> Eskiden <c>Show(null, …)</c>
    /// yalnızca <c>HasCommit</c>'i kapatıyor, <c>Badges</c> ile <c>Parents</c> kapatılan
    /// deponun nesnelerini tutmaya devam ediyordu. Panel gizli olduğu için gözle
    /// görülmüyordu; kapalı bir depoya ait rozetler bellekte duruyordu.
    /// <para>
    /// Bu, zayıf referans testiyle <b>yakalanamayan</b> bir durum: satırın kendisi
    /// zaten başka yerden de bırakılıyor. Ölçülebilir olan, panelin açıkça boş kalması.
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
    /// Kapatma sayaçları da sıfırlamalı; sıfırlamazsa arayüz kapalı bir depo için
    /// "N commit yüklendi" göstermeye devam eder — veri gitmiş, sayı kalmış olur.
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
    /// Aynı depoyu tekrar tekrar açmak satır sayısını <b>artırmamalı</b>. Artıyorsa
    /// açılış eskisini temizlemiyor demektir ve tazeleme yapan bir oturumda liste
    /// katlanarak büyürdü.
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
