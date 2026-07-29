using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P02-T09 — Ref okuma. Biçimlerin hepsi gerçek <c>git</c> ile ölçülüp buna göre yazıldı.
/// </summary>
public class RefReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<RefReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new RefReader(new GitProcessRunner(executable));
    }

    [Fact]
    public async Task Dogmamis_repo_cokmeden_okunur()
    {
        // Kullanıcının ilk açtığı depo bu olabilir: HEAD var olmayan bir dala işaret ediyor.
        using TestRepository repository = TestRepository.CreateEmpty();
        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(repository.Path, Ct);

        refs.Head.IsUnborn.ShouldBeTrue();
        refs.Head.IsDetached.ShouldBeFalse();
        refs.Head.BranchName.ShouldBe("main");
        refs.Head.Commit.IsEmpty.ShouldBeTrue();
        refs.LocalBranches.ShouldBeEmpty();
        refs.CurrentBranch.ShouldBeNull();
    }

    [Fact]
    public async Task Yerel_dallar_ve_gecerli_dal_okunur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("branch", "ikinci");
        repository.Git("branch", "ucuncu");

        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(repository.Path, Ct);

        refs.LocalBranches.Select(b => b.Name).ShouldBe(["ikinci", "main", "ucuncu"], ignoreOrder: true);
        refs.Head.IsDetached.ShouldBeFalse();
        refs.Head.BranchName.ShouldBe("main");
        refs.CurrentBranch!.Name.ShouldBe("main");
        refs.LocalBranches.Count(b => b.IsCurrent).ShouldBe(1);
    }

    [Fact]
    public async Task Detached_HEAD_dogru_raporlanir()
    {
        // Ölçüldü: detached durumda %(HEAD) HİÇBİR dal için "*" dönmüyor.
        // Bu yüzden symbolic-ref ile ayrıca soruyoruz.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("commit", "--allow-empty", "-m", "ikinci");
        repository.Git("checkout", "--detach", "HEAD~1");

        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(repository.Path, Ct);

        refs.Head.IsDetached.ShouldBeTrue();
        refs.Head.IsUnborn.ShouldBeFalse();
        refs.Head.BranchName.ShouldBeNull();
        refs.Head.Commit.IsFull.ShouldBeTrue();
        refs.CurrentBranch.ShouldBeNull();
        refs.LocalBranches.ShouldAllBe(b => !b.IsCurrent);
    }

    [Fact]
    public async Task Hafif_ve_annotated_taglar_ayirt_edilir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("tag", "hafif");
        repository.Git("tag", "-a", "imzali", "-m", "tag mesajı burada");

        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(repository.Path, Ct);

        TagInfo lightweight = refs.Tags.Single(t => t.Name == "hafif");
        TagInfo annotated = refs.Tags.Single(t => t.Name == "imzali");

        lightweight.IsAnnotated.ShouldBeFalse();
        annotated.IsAnnotated.ShouldBeTrue();
        annotated.Subject.ShouldBe("tag mesajı burada");

        // KRİTİK: annotated tag'de ObjectId tag NESNESİdir, commit değil.
        // TargetCommit ise %(*objectname) ile çözülmüş gerçek commit.
        annotated.Ref.ObjectId.ShouldNotBe(annotated.Ref.TargetCommit);
        annotated.Ref.TargetCommit.ShouldBe(lightweight.Ref.TargetCommit);

        // Hafif tag'de ikisi aynı olmalı.
        lightweight.Ref.ObjectId.ShouldBe(lightweight.Ref.TargetCommit);
    }

    [Theory]
    [InlineData("", 0, 0, false)]
    [InlineData("[ahead 3]", 3, 0, false)]
    [InlineData("[behind 4]", 0, 4, false)]
    [InlineData("[ahead 3, behind 2]", 3, 2, false)]
    [InlineData("[gone]", 0, 0, true)]
    public void Upstream_takip_bicimleri_ayristirilir(string value, int ahead, int behind, bool gone)
    {
        UpstreamTracking tracking = RefReader.ParseTracking(value);

        tracking.Ahead.ShouldBe(ahead);
        tracking.Behind.ShouldBe(behind);
        tracking.IsGone.ShouldBe(gone);
    }

    [Fact]
    public void Ayrilmis_dal_diverged_olarak_isaretlenir()
    {
        UpstreamTracking tracking = RefReader.ParseTracking("[ahead 3, behind 2]");

        tracking.IsDiverged.ShouldBeTrue();
        tracking.IsUpToDate.ShouldBeFalse();
    }

    [Fact]
    public async Task Uzak_dallar_ve_upstream_takibi_okunur()
    {
        using TestRepository remote = TestRepository.CreateBare();
        using TestRepository local = TestRepository.CreateWithSingleCommit();

        local.Git("remote", "add", "origin", remote.Path);
        local.Git("push", "-q", "origin", "main");
        local.Git("branch", "--set-upstream-to=origin/main", "main");

        // Yereli iki commit ileri al.
        local.Git("commit", "--allow-empty", "-m", "ileri bir");
        local.Git("commit", "--allow-empty", "-m", "ileri iki");

        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(local.Path, Ct);

        BranchInfo main = refs.LocalBranches.Single(b => b.Name == "main");
        main.Upstream.ShouldBe("origin/main");
        main.Tracking.Ahead.ShouldBe(2);
        main.Tracking.Behind.ShouldBe(0);
        main.Tracking.IsUpToDate.ShouldBeFalse();

        refs.RemoteBranches.ShouldContain(b => b.Name == "origin/main");
        refs.RemoteBranches.ShouldAllBe(b => b.IsRemote);
    }

    [Fact]
    public async Task Upstream_i_olmayan_dal_null_upstream_dondurur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("branch", "yalniz");

        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(repository.Path, Ct);

        BranchInfo alone = refs.LocalBranches.Single(b => b.Name == "yalniz");
        alone.Upstream.ShouldBeNull();
        alone.Tracking.ShouldBe(UpstreamTracking.None);
    }

    [Fact]
    public async Task Remote_yoksa_bos_liste_doner()
    {
        // `git config --get-regexp` eşleşme yoksa 1 döner; bu hata sayılmamalı.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(repository.Path, Ct);

        refs.Remotes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Remote_url_ve_pushurl_okunur()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("remote", "add", "origin", "https://example.invalid/repo.git");
        repository.Git("remote", "add", "yedek", "https://example.invalid/yedek.git");
        repository.Git("remote", "set-url", "--push", "origin", "ssh://git@example.invalid/repo.git");

        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(repository.Path, Ct);

        refs.Remotes.Count.ShouldBe(2);

        RemoteInfo origin = refs.Remotes.Single(r => r.Name == "origin");
        origin.FetchUrl.ShouldBe("https://example.invalid/repo.git");
        origin.PushUrl.ShouldBe("ssh://git@example.invalid/repo.git");

        RemoteInfo backup = refs.Remotes.Single(r => r.Name == "yedek");
        backup.FetchUrl.ShouldBe(backup.PushUrl);
    }

    [Fact]
    public async Task Unicode_ve_egik_cizgili_dal_adlari_okunur()
    {
        // Dal adları / içerebilir (feature/x) ve UTF-8 olabilir.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("branch", "özellik/çalışma-günlüğü");

        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(repository.Path, Ct);

        BranchInfo branch = refs.LocalBranches.Single(b => b.Name.StartsWith('ö'));
        branch.Name.ShouldBe("özellik/çalışma-günlüğü");
        branch.Ref.FullName.ShouldBe("refs/heads/özellik/çalışma-günlüğü");
    }

    [Fact]
    public async Task Stash_ref_i_dal_veya_tag_olarak_sayilmaz()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.WriteFile("a.txt", "değişiklik\n");
        repository.Git("stash", "push", "-m", "test stash");

        RefReader reader = await CreateReaderAsync();

        RepositoryRefs refs = await reader.ReadAsync(repository.Path, Ct);

        refs.LocalBranches.ShouldNotContain(b => b.Name.Contains("stash", StringComparison.Ordinal));
        refs.Tags.ShouldNotContain(t => t.Name.Contains("stash", StringComparison.Ordinal));
    }
}
