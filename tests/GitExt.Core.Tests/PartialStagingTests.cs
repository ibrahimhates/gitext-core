using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P05-T04 / P05-T05 — Yama tabanlı kısmi stage. <b>Fazın en riskli kodu.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>Test yaklaşımı (plan gereği):</b> yamanın metni değil <b>etkisi</b> doğrulanıyor —
/// yama uygulanır, sonra index'in içeriği <c>git show :&lt;yol&gt;</c> ile okunup beklenen
/// sonuçla karşılaştırılır.
/// </para>
/// <para>
/// <b>ÖLÇÜLDÜ — risk dağılımı:</b> hunk başlığındaki sayı hataları (<c>corrupt patch</c>) ve
/// bağlam uyuşmazlıkları (<c>patch failed</c>) git tarafından <b>reddediliyor</b>. Ama
/// <b>seçim mantığı hatası</b> yakalanmıyor: yama geçerli olduğu için git kabul eder ve
/// sessizce yanlış içerik stage'lenir. Ölçümde, seçilmeyen bir <c>-</c> satırı bağlama
/// çevrilmeyince o satır index'ten kayboldu. Aşağıdaki testlerin çoğu bu sınıfı hedefliyor.
/// </para>
/// </remarks>
public class PartialStagingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        StagingWriter Writer,
        DiffReader Reader,
        GitWriteQueue Queue) : IDisposable
    {
        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }

        /// <summary>Çalışma ağacı ↔ index farkı (kısmi stage'in kaynağı).</summary>
        public Task<IReadOnlyList<FileDiff>> UnstagedAsync() =>
            Reader.ReadUnstagedAsync(Repository.Path, cancellationToken: Ct);

        /// <summary>Index ↔ HEAD farkı (kısmi unstage'in kaynağı).</summary>
        public Task<IReadOnlyList<FileDiff>> StagedAsync() =>
            Reader.ReadStagedAsync(Repository.Path, cancellationToken: Ct);

        /// <summary>Index'teki dosya içeriği.</summary>
        public string Indexed(string path) => Repository.Git("show", $":{path}");

        /// <summary>Çalışma ağacındaki dosya içeriği.</summary>
        public string OnDisk(string path) =>
            File.ReadAllText(Path.Combine(Repository.Path, path));
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return new Harness(
            repository,
            new StagingWriter(new GitWriter(runner, queue), runner),
            new DiffReader(runner),
            queue);
    }

    /// <summary>Beş satırlık bir dosya kurar ve iki ayrı yerini değiştirir.</summary>
    private static void SetupTwoChanges(Harness harness)
    {
        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\ndort\nbes\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        harness.Repository.WriteFile("a.txt", "bir\nIKI\nuc\nDORT\nbes\n");
    }

    /// <summary>Bir hunk içinde belirli türdeki satırların indekslerini verir.</summary>
    private static PatchSelection SelectLines(FileDiff diff, int hunkIndex, params int[] lineIndexes) =>
        PatchSelection.Lines(lineIndexes.Select(line => (hunkIndex, line)));

    [Fact]
    public async Task Tek_hunk_stage_lenir_calisma_agaci_bozulmaz()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "bir\nIKI\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.Hunks(diff, 0), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\nIKI\n");

        // `--cached` çalışma ağacına DOKUNMAZ.
        harness.OnDisk("a.txt").ShouldBe("bir\nIKI\n");
    }

    [Fact]
    public async Task SECILMEYEN_degisiklik_index_te_ESKI_haliyle_kalir()
    {
        // 🔴 Asıl risk bu: seçilmeyen bir `-` satırı bağlama çevrilmezse index'ten
        // KAYBOLUR ve git bunu yakalamaz (yama geçerlidir).
        using Harness harness = await CreateAsync();
        SetupTwoChanges(harness);

        IReadOnlyList<FileDiff> diffs = await harness.UnstagedAsync();
        FileDiff diff = diffs.Single();
        DiffHunk hunk = diff.Hunks.Single();

        // Yalnızca ikinci değişikliği (dort → DORT) seç.
        int[] second = [.. Enumerable
            .Range(0, hunk.Lines.Count)
            .Where(i => hunk.Lines[i].Content is "dort" or "DORT")];

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, second), cancellationToken: Ct);

        // "iki" satırı DEĞİŞMEDEN durmalı; "dort" ise güncellenmiş olmalı.
        harness.Indexed("a.txt").ShouldBe("bir\niki\nuc\nDORT\nbes\n");
    }

    [Fact]
    public async Task Tek_bir_satir_stage_lenebilir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        // Üç satır da ekleniyor; yalnızca ortadaki stage'lenecek.
        harness.Repository.WriteFile("a.txt", "bir\nA\niki\nB\nuc\nC\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        DiffHunk hunk = diff.Hunks.Single();

        int bIndex = Enumerable.Range(0, hunk.Lines.Count).Single(i => hunk.Lines[i].Content == "B");

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, bIndex), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\niki\nB\nuc\n");
    }

    [Fact]
    public async Task Dosyanin_ILK_satiri_stage_lenebilir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "BIR\niki\nUC\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        DiffHunk hunk = diff.Hunks.Single();

        int[] first = [.. Enumerable
            .Range(0, hunk.Lines.Count)
            .Where(i => hunk.Lines[i].Content is "bir" or "BIR")];

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, first), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("BIR\niki\nuc\n");
    }

    [Fact]
    public async Task Dosyanin_SON_satiri_stage_lenebilir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "BIR\niki\nUC\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        DiffHunk hunk = diff.Hunks.Single();

        int[] last = [.. Enumerable
            .Range(0, hunk.Lines.Count)
            .Where(i => hunk.Lines[i].Content is "uc" or "UC")];

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, last), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\niki\nUC\n");
    }

    [Fact]
    public async Task Bitisik_olmayan_iki_hunk_tan_YALNIZCA_biri_stage_lenir()
    {
        using Harness harness = await CreateAsync();

        // Hunk'ların ayrı kalması için aralarında bol bağlam bırakılıyor.
        string original = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"satir{i}")) + "\n";
        harness.Repository.WriteFile("a.txt", original);
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        string[] lines = original.Split('\n');
        lines[0] = "BAS";
        lines[35] = "SON";
        harness.Repository.WriteFile("a.txt", string.Join('\n', lines));

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        diff.Hunks.Count.ShouldBeGreaterThan(1);

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.Hunks(diff, 1), cancellationToken: Ct);

        string indexed = harness.Indexed("a.txt");

        // İkinci hunk uygulandı, birincisi uygulanmadı.
        indexed.ShouldContain("SON");
        indexed.ShouldNotContain("BAS");
        indexed.ShouldContain("satir1\n");
    }

    [Fact]
    public async Task Iki_hunk_birlikte_stage_lenir()
    {
        using Harness harness = await CreateAsync();

        string original = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"satir{i}")) + "\n";
        harness.Repository.WriteFile("a.txt", original);
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");

        string[] lines = original.Split('\n');
        lines[0] = "BAS";
        lines[35] = "SON";
        harness.Repository.WriteFile("a.txt", string.Join('\n', lines));

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        // İkinci hunk'ın başlığındaki YENİ başlangıç, birincinin satır farkına göre kayar.
        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe(harness.OnDisk("a.txt"));
    }

    [Fact]
    public async Task Dosya_sonunda_NEWLINE_YOKKEN_calisir()
    {
        // İşaret satırı (`\ No newline at end of file`) hem eski hem yeni tarafta çıkabiliyor
        // (P04-T01'de ölçüldü); yamada yanlış yere konursa git reddeder.
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "bir\nIKI");

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\nIKI");
    }

    [Fact]
    public async Task CRLF_satir_sonlari_korunur()
    {
        using Harness harness = await CreateAsync();

        // ⚠️ `WriteFile` satır sonlarını LF'e sabitliyor (fixture kararı); CRLF testi ham
        // bayt yazmak zorunda, aksi hâlde test aslında CRLF'i hiç denemez.
        string path = Path.Combine(harness.Repository.Path, "a.txt");

        File.WriteAllBytes(path, "bir\r\niki\r\n"u8.ToArray());
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        File.WriteAllBytes(path, "bir\r\nIKI\r\n"u8.ToArray());

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\r\nIKI\r\n");
    }

    [Fact]
    public async Task ASCII_disi_yol_ve_icerik_calisir()
    {
        // Yol tırnaklanmadan veriliyor (ölçüldü: `git apply` ham UTF-8 kabul ediyor).
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("türkçe dosya.txt", "ilk satır\nikinci\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("türkçe dosya.txt", "ilk satır\nİKİNCİ ÖĞE\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.All(diff), cancellationToken: Ct);

        harness.Indexed("türkçe dosya.txt").ShouldBe("ilk satır\nİKİNCİ ÖĞE\n");
    }

    [Fact]
    public async Task Yalnizca_SILINEN_satir_stage_lenir()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.WriteFile("a.txt", "bir\niki\nuc\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Git("commit", "-m", "ilk");
        harness.Repository.WriteFile("a.txt", "bir\n");

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        DiffHunk hunk = diff.Hunks.Single();

        int ikiIndex = Enumerable.Range(0, hunk.Lines.Count).Single(i => hunk.Lines[i].Content == "iki");

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, SelectLines(diff, 0, ikiIndex), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\nuc\n");
    }

    // ---- Kısmi unstage (ters yön) ----

    [Fact]
    public async Task Kismi_UNSTAGE_secilen_satiri_geri_alir()
    {
        // Ters yönde kurallar simetrik olarak DEĞİŞİYOR: seçilmeyen `+` bağlama çevrilir,
        // seçilmeyen `-` atlanır. Karıştırılırsa index sessizce bozulur.
        using Harness harness = await CreateAsync();
        SetupTwoChanges(harness);

        harness.Repository.Git("add", "-A");
        harness.Indexed("a.txt").ShouldBe("bir\nIKI\nuc\nDORT\nbes\n");

        FileDiff staged = (await harness.StagedAsync()).Single();
        DiffHunk hunk = staged.Hunks.Single();

        int[] firstChange = [.. Enumerable
            .Range(0, hunk.Lines.Count)
            .Where(i => hunk.Lines[i].Content is "iki" or "IKI")];

        await harness.Writer.UnstagePartialAsync(
            harness.Repository.Path, staged, SelectLines(staged, 0, firstChange), cancellationToken: Ct);

        // İlk değişiklik geri alındı, ikincisi index'te kaldı.
        harness.Indexed("a.txt").ShouldBe("bir\niki\nuc\nDORT\nbes\n");

        // Çalışma ağacı dokunulmadan durmalı.
        harness.OnDisk("a.txt").ShouldBe("bir\nIKI\nuc\nDORT\nbes\n");
    }

    [Fact]
    public async Task Bos_secim_HICBIR_SEY_yapmaz()
    {
        using Harness harness = await CreateAsync();
        SetupTwoChanges(harness);

        FileDiff diff = (await harness.UnstagedAsync()).Single();

        await harness.Writer.StagePartialAsync(
            harness.Repository.Path, diff, PatchSelection.Lines([]), cancellationToken: Ct);

        harness.Indexed("a.txt").ShouldBe("bir\niki\nuc\ndort\nbes\n");
    }

    [Fact]
    public async Task Uretilen_yama_gitin_kendi_dogrulamasindan_gecer()
    {
        // git'in bize sunduğu güvenlik ağı: bozuk yama reddedilir. Bu test o ağın
        // gerçekten kurulu olduğunu (ve `--recount` ile kapatılmadığını) doğruluyor.
        using Harness harness = await CreateAsync();
        SetupTwoChanges(harness);

        FileDiff diff = (await harness.UnstagedAsync()).Single();
        string patch = PatchBuilder.Build(diff, PatchSelection.All(diff), PatchDirection.Stage)
            .ShouldNotBeNull();

        string patchFile = Path.Combine(harness.Repository.Path, "..", "uretilen.patch");
        File.WriteAllText(patchFile, patch);

        try
        {
            // `--check` uygulamadan doğrular.
            harness.Repository.Git("apply", "--cached", "--check", patchFile);
        }
        finally
        {
            File.Delete(patchFile);
        }
    }
}
