using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T16 · T17 · T18 · T19 · T20 · T21 — blame, file history, tag, submodule,
/// worktree and search.
/// </summary>
public class RepositoryExplorerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<GitProcessRunner> RunnerAsync() =>
        new(await GitExecutable.LocateAsync(cancellationToken: Ct));

    /// <summary>Sets up a file with three commits that has been renamed once.</summary>
    private static TestRepository RenamedFile()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("f.txt", "bir\niki\nuc\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "ilk");

        repository.WriteFile("f.txt", "bir\nDEGISTI\nuc\n");
        repository.Git("commit", "-am", "ikinci");

        repository.Git("mv", "f.txt", "yeni-ad.txt");
        repository.Git("commit", "-m", "yeniden adlandirildi");

        return repository;
    }

    // ==================================================== P07-T16 blame

    [Fact]
    public async Task Blame_her_satirin_kaynagini_veriyor()
    {
        using TestRepository repository = RenamedFile();
        BlameReader reader = new(await RunnerAsync());

        IReadOnlyList<BlameLine> lines = await reader.ReadAsync(
            repository.Path, RepositoryPath.Parse("yeni-ad.txt"), cancellationToken: Ct);

        lines.Count.ShouldBe(3);
        lines[0].Content.ShouldBe("bir");
        lines[1].Content.ShouldBe("DEGISTI");
        lines[0].LineNumber.ShouldBe(1);
        lines[1].LineNumber.ShouldBe(2);

        // Lines 1 and 2 come from DIFFERENT commits.
        lines[0].ObjectId.ShouldNotBe(lines[1].ObjectId);
    }

    [Fact]
    public async Task AYNI_committen_gelen_satirlarin_YAZARI_bos_kalmıyor()
    {
        // 🔴 MEASURED: `--porcelain` writes the metadata ONCE per commit. On a second line coming
        // from the same commit, `author`/`summary` are not repeated; a reader that parses each line
        // independently would show those lines' author as EMPTY — and in the most common case at
        // that.
        using TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("f.txt", "bir\niki\nuc\ndort\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-m", "hepsi tek committe");

        BlameReader reader = new(await RunnerAsync());

        IReadOnlyList<BlameLine> lines = await reader.ReadAsync(
            repository.Path, RepositoryPath.Parse("f.txt"), cancellationToken: Ct);

        lines.Count.ShouldBe(4);
        lines.ShouldAllBe(line => line.AuthorName == "gitext-core tests");
        lines.ShouldAllBe(line => line.Summary == "hepsi tek committe");
    }

    [Fact]
    public async Task Blame_YENIDEN_ADLANDIRMADAN_onceki_dosya_adini_tasiyor()
    {
        using TestRepository repository = RenamedFile();
        BlameReader reader = new(await RunnerAsync());

        IReadOnlyList<BlameLine> lines = await reader.ReadAsync(
            repository.Path, RepositoryPath.Parse("yeni-ad.txt"), cancellationToken: Ct);

        // The lines were written under the old name; "go to previous version" uses this.
        lines[0].FileName.ShouldBe("f.txt");
    }

    [Fact]
    public async Task Blame_BELIRLI_bir_surumden_okunabiliyor()
    {
        using TestRepository repository = RenamedFile();
        BlameReader reader = new(await RunnerAsync());

        IReadOnlyList<BlameLine> lines = await reader.ReadAsync(
            repository.Path, RepositoryPath.Parse("f.txt"), "HEAD~1", Ct);

        lines.Count.ShouldBe(3);
        lines[1].Content.ShouldBe("DEGISTI");
    }

    [Fact]
    public async Task Olmayan_dosyada_blame_BOS_donduruyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        BlameReader reader = new(await RunnerAsync());

        (await reader.ReadAsync(repository.Path, RepositoryPath.Parse("yok.txt"), cancellationToken: Ct))
            .ShouldBeEmpty();
    }

    // ============================================== P07-T17 file history

    [Fact]
    public async Task Dosya_gecmisi_YENIDEN_ADLANDIRMA_boyunca_takip_ediyor()
    {
        // MEASURED: 3 commits with `--follow`, 1 without it. The user would think "so this is all
        // the history this file has".
        using TestRepository repository = RenamedFile();
        FileHistoryReader reader = new(await RunnerAsync());

        IReadOnlyList<FileHistoryEntry> history = await reader.ReadAsync(
            repository.Path, RepositoryPath.Parse("yeni-ad.txt"), cancellationToken: Ct);

        history.Count.ShouldBe(3);
        history.Select(entry => entry.Subject)
            .ShouldBe(["yeniden adlandirildi", "ikinci", "ilk"]);
    }

    [Fact]
    public async Task Yeniden_adlandirma_DOGRU_committe_isaretleniyor()
    {
        // 🔴 MEASURED: with the record separator at the END, `--name-status` lines fell into the
        // beginning of the NEXT chunk; a rename would be attributed to the wrong commit.
        // The separator was moved to the front.
        using TestRepository repository = RenamedFile();
        FileHistoryReader reader = new(await RunnerAsync());

        IReadOnlyList<FileHistoryEntry> history = await reader.ReadAsync(
            repository.Path, RepositoryPath.Parse("yeni-ad.txt"), cancellationToken: Ct);

        history[0].IsRename.ShouldBeTrue("adlandırma EN ÜSTTEKİ commit'te oldu");
        history[0].Path.ShouldBe("f.txt", "eski ad gösterilmeli");

        history[1].IsRename.ShouldBeFalse();
        history[2].IsRename.ShouldBeFalse();
    }

    // ================================================== P07-T18 tag

    [Fact]
    public async Task Hafif_ve_aciklamali_etiketler_AYIRT_ediliyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("tag", "hafif");
        repository.Git("tag", "-a", "aciklamali", "-m", "aciklama metni");

        GitProcessRunner runner = await RunnerAsync();
        using GitWriteQueue queue = new();
        TagWriter writer = new(new GitWriter(runner, queue), runner);

        IReadOnlyList<GitTag> tags = await writer.ListAsync(repository.Path, Ct);

        tags.Count.ShouldBe(2);

        GitTag annotated = tags.First(tag => tag.Name == "aciklamali");
        annotated.IsAnnotated.ShouldBeTrue();
        annotated.Message.ShouldBe("aciklama metni");

        tags.First(tag => tag.Name == "hafif").IsAnnotated.ShouldBeFalse();
    }

    [Fact]
    public async Task Aciklamali_etiket_ETIKET_nesnesini_degil_COMMITI_gosteriyor()
    {
        // On an annotated tag `%(objectname)` is the SHA of the TAG OBJECT. Mixing them up meant
        // going to a non-existent commit when clicking the tag.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("tag", "-a", "v1", "-m", "surum");

        string head = repository.Git("rev-parse", "HEAD").Trim();

        GitProcessRunner runner = await RunnerAsync();
        using GitWriteQueue queue = new();
        TagWriter writer = new(new GitWriter(runner, queue), runner);

        IReadOnlyList<GitTag> tags = await writer.ListAsync(repository.Path, Ct);

        tags.ShouldHaveSingleItem().ObjectId.ShouldBe(head);
    }

    [Fact]
    public async Task Etiket_olusturuluyor_ve_siliniyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        GitProcessRunner runner = await RunnerAsync();
        using GitWriteQueue queue = new();
        TagWriter writer = new(new GitWriter(runner, queue), runner);

        await writer.CreateAsync(
            repository.Path, new TagOptions { Name = "v1", Message = "ilk sürüm" }, Ct);

        IReadOnlyList<GitTag> tags = await writer.ListAsync(repository.Path, Ct);
        tags.ShouldHaveSingleItem().IsAnnotated.ShouldBeTrue();

        await writer.DeleteAsync(repository.Path, "v1", Ct);

        (await writer.ListAsync(repository.Path, Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Mesajsiz_etiket_HAFIF_olusuyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        GitProcessRunner runner = await RunnerAsync();
        using GitWriteQueue queue = new();
        TagWriter writer = new(new GitWriter(runner, queue), runner);

        await writer.CreateAsync(repository.Path, new TagOptions { Name = "hafif" }, Ct);

        (await writer.ListAsync(repository.Path, Ct))
            .ShouldHaveSingleItem().IsAnnotated.ShouldBeFalse();
    }

    // =============================================== P07-T20 worktree

    [Fact]
    public async Task Worktreeler_listeleniyor_ve_ANA_olan_isaretleniyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        using TestRepository linked = repository.AddWorkTree("ikinci-agac");

        GitProcessRunner runner = await RunnerAsync();
        using GitWriteQueue queue = new();
        WorkTreeReader reader = new(new GitWriter(runner, queue), runner);

        IReadOnlyList<WorkTree> trees = await reader.ListAsync(repository.Path, Ct);

        trees.Count.ShouldBe(2);
        trees[0].IsMain.ShouldBeTrue();
        trees[0].BranchName.ShouldBe("main");
        trees[1].IsMain.ShouldBeFalse();
        trees[1].BranchName.ShouldBe("ikinci-agac", "refs/heads/ önekі atılmalı");
    }

    [Fact]
    public void Ayrik_HEADli_worktree_dogru_okunuyor()
    {
        const string output = """
            worktree /depo
            HEAD abc123
            branch refs/heads/main

            worktree /depo/wt
            HEAD def456
            detached

            """;

        IReadOnlyList<WorkTree> trees = WorkTreeReader.Parse(output);

        trees.Count.ShouldBe(2);
        trees[1].IsDetached.ShouldBeTrue();
        trees[1].BranchName.ShouldBeNull();
        trees[1].ObjectId.ShouldBe("def456");
    }

    [Fact]
    public void Kilitli_worktree_isaretleniyor()
    {
        // A locked worktree cannot be removed; showing the button enabled would be wrong.
        const string output = """
            worktree /depo
            HEAD abc123
            branch refs/heads/main

            worktree /depo/wt
            HEAD def456
            branch refs/heads/yan
            locked

            """;

        WorkTreeReader.Parse(output)[1].IsLocked.ShouldBeTrue();
    }

    [Fact]
    public async Task Worktree_ekleniyor_ve_kaldiriliyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        GitProcessRunner runner = await RunnerAsync();
        using GitWriteQueue queue = new();
        WorkTreeReader reader = new(new GitWriter(runner, queue), runner);

        string path = Path.Combine(Path.GetTempPath(), $"gitext-wt-{Guid.NewGuid():N}"[..24]);

        await reader.AddAsync(repository.Path, path, "yeni-dal", createBranch: true, Ct);
        (await reader.ListAsync(repository.Path, Ct)).Count.ShouldBe(2);

        await reader.RemoveAsync(repository.Path, path, force: false, Ct);
        (await reader.ListAsync(repository.Path, Ct)).ShouldHaveSingleItem();
    }

    // ============================================== P07-T19 submodule

    [Fact]
    public void Submodule_durumlari_BASTAKI_isaretten_okunuyor()
    {
        const string output =
            "-abc123 dis/modul\n"
            + "+def456 dis/digeri (v1.0-2-gabc)\n"
            + " 789abc dis/guncel (v2.0)\n"
            + "U000111 dis/cakisik\n";

        IReadOnlyList<Submodule> modules = SubmoduleReader.Parse(output);

        modules.Count.ShouldBe(4);
        modules[0].Status.ShouldBe(SubmoduleStatusKind.NotInitialized);
        modules[1].Status.ShouldBe(SubmoduleStatusKind.Modified);
        modules[1].Describe.ShouldBe("v1.0-2-gabc");
        modules[2].Status.ShouldBe(SubmoduleStatusKind.UpToDate);
        modules[3].Status.ShouldBe(SubmoduleStatusKind.Conflicted);
    }

    [Fact]
    public void BOSLUKLU_submodule_yolu_bozulmuyor()
    {
        // The path can contain spaces; the describe is in the TRAILING parentheses.
        const string output = " abc123 dis/bir dizin adi (v1.0)\n";

        Submodule module = SubmoduleReader.Parse(output).ShouldHaveSingleItem();

        module.Path.Value.ShouldBe("dis/bir dizin adi");
        module.Describe.ShouldBe("v1.0");
    }

    [Fact]
    public async Task Gercek_submodule_okunuyor()
    {
        using TestRepository outer = TestRepository.CreateWithSingleCommit();
        using TestRepository inner = TestRepository.CreateWithSingleCommit();

        outer.AddSubmodule(inner, "dis");
        outer.Git("commit", "-m", "submodule eklendi");

        GitProcessRunner runner = await RunnerAsync();
        using GitWriteQueue queue = new();
        SubmoduleReader reader = new(new GitWriter(runner, queue), runner);

        IReadOnlyList<Submodule> modules = await reader.ListAsync(outer.Path, Ct);

        modules.ShouldHaveSingleItem().Path.Value.ShouldBe("dis");
    }

    [Fact]
    public async Task Alt_modulsuz_depoda_git_HIC_CALISTIRILMIYOR()
    {
        // 🔴 MEASURED (P12-T13): `git submodule status` costs 12-49 ms in a repository with NO
        // submodules — it is one of the few git commands that is still a shell script, so the
        // price is the shell rather than the work. The left panel refreshes on every repository
        // change, so that would be tens of milliseconds per refresh for a list that is always
        // empty. A repository without `.gitmodules` has no submodules by definition.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        InMemoryGitCommandLog log = new();
        GitProcessRunner logged = new(await GitExecutable.LocateAsync(cancellationToken: Ct), log);
        using GitWriteQueue queue = new();
        SubmoduleReader reader = new(new GitWriter(logged, queue), logged);

        IReadOnlyList<Submodule> modules = await reader.ListAsync(repository.Path, Ct);

        modules.ShouldBeEmpty();
        log.Entries.ShouldBeEmpty("git süreci hiç başlatılmamalı");
    }

    // ================================================= P07-T21 arama

    [Fact]
    public async Task Commit_mesajinda_araniyor()
    {
        using TestRepository repository = RenamedFile();
        SearchReader reader = new(await RunnerAsync());

        IReadOnlyList<string> found = await reader.SearchCommitsAsync(
            repository.Path, new CommitSearchQuery { Message = "ikinci" }, cancellationToken: Ct);

        found.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task PICKAXE_ile_icerik_degisimi_bulunuyor()
    {
        using TestRepository repository = RenamedFile();
        SearchReader reader = new(await RunnerAsync());

        IReadOnlyList<string> found = await reader.SearchCommitsAsync(
            repository.Path,
            new CommitSearchQuery { ContentAdded = "DEGISTI" },
            cancellationToken: Ct);

        found.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Yazara_gore_araniyor()
    {
        using TestRepository repository = RenamedFile();
        SearchReader reader = new(await RunnerAsync());

        IReadOnlyList<string> found = await reader.SearchCommitsAsync(
            repository.Path, new CommitSearchQuery { Author = "gitext-core" }, cancellationToken: Ct);

        found.Count.ShouldBe(3);
    }

    [Fact]
    public async Task BOS_sorgu_tum_gecmisi_DONDURMUYOR()
    {
        // Returning every commit for an empty query would read like a "search result".
        using TestRepository repository = RenamedFile();
        SearchReader reader = new(await RunnerAsync());

        (await reader.SearchCommitsAsync(repository.Path, new CommitSearchQuery(), cancellationToken: Ct))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Dosya_iceriginde_araniyor()
    {
        using TestRepository repository = RenamedFile();
        SearchReader reader = new(await RunnerAsync());

        IReadOnlyList<ContentMatch> matches =
            await reader.SearchContentAsync(repository.Path, "DEGISTI", cancellationToken: Ct);

        ContentMatch match = matches.ShouldHaveSingleItem();
        match.Path.ShouldBe("yeni-ad.txt");
        match.LineNumber.ShouldBe(2);
        match.Line.ShouldBe("DEGISTI");
    }

    [Fact]
    public async Task Eslesme_yoksa_HATA_degil_BOS_liste()
    {
        // `git grep` gives exit code 1 when there is no match; that is not an error.
        using TestRepository repository = RenamedFile();
        SearchReader reader = new(await RunnerAsync());

        (await reader.SearchContentAsync(repository.Path, "boyle-bir-sey-yok", cancellationToken: Ct))
            .ShouldBeEmpty();
    }

    [Fact]
    public void IKI_NOKTA_iceren_yol_arama_sonucunu_KAYDIRMIYOR()
    {
        // Without `-z`, parsing `path:line:content` would slip on a path containing a colon.
        const string output = "tuhaf:ad.txt 12 icerik: iki nokta var\n";

        ContentMatch match = SearchReader.Parse(output).ShouldHaveSingleItem();

        match.Path.ShouldBe("tuhaf:ad.txt");
        match.LineNumber.ShouldBe(12);
        match.Line.ShouldBe("icerik: iki nokta var");
    }
}
