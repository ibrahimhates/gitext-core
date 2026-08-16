using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T05 — remote writing.
/// </summary>
/// <remarks>
/// The weight is on the two irreversible paths the measurement exposed: the configuration
/// <c>remove</c> silently deletes, and the refspec <c>rename</c> leaves un-updated while returning
/// exit code 0.
/// </remarks>
public class RemoteWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        RemoteWriter Writer,
        RemoteReader Reader,
        GitWriteQueue Queue) : IDisposable
    {
        public string Path => Repository.Path;

        public void Dispose()
        {
            Queue.Dispose();
            Repository.Dispose();
        }
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateWithSingleCommit();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();
        GitWriter gitWriter = new(runner, queue);
        RemoteReader reader = new(runner);

        return new Harness(repository, new RemoteWriter(gitWriter, runner, reader), reader, queue);
    }

    /// <summary>Produces a remote repository that can really be fetched from.</summary>
    private static TestRepository CreateUpstream(out string url)
    {
        TestRepository upstream = TestRepository.CreateBare();
        url = upstream.Path;
        return upstream;
    }

    [Fact]
    public async Task Ekleme_ve_okuma()
    {
        using Harness harness = await CreateAsync();

        await harness.Writer.AddAsync(
            harness.Path,
            new RemoteAddOptions { Name = "origin", Url = "https://example.com/a.git" },
            Ct);

        GitRemote remote = (await harness.Reader.FindAsync(harness.Path, "origin", Ct))!;
        remote.Url.ShouldBe("https://example.com/a.git");
        remote.HasDefaultFetchRefspec.ShouldBeTrue();
    }

    [Fact]
    public async Task Gecersiz_ad_git_e_HIC_gitmiyor()
    {
        using Harness harness = await CreateAsync();

        await Should.ThrowAsync<ArgumentException>(() => harness.Writer.AddAsync(
            harness.Path,
            new RemoteAddOptions { Name = "a b", Url = "https://example.com/a.git" },
            Ct));
    }

    [Fact]
    public async Task Var_olan_ad_RemoteAlreadyExists_olarak_siniflandiriliyor()
    {
        // 🔴 The reason this test exists: "error: remote origin already exists." also matches the
        // generic "already exists" pattern, and the user would be told "A BRANCH with this name
        // already exists.".
        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", "https://example.com/a.git");

        GitException error = await Should.ThrowAsync<GitException>(() => harness.Writer.AddAsync(
            harness.Path,
            new RemoteAddOptions { Name = "origin", Url = "https://example.com/b.git" },
            Ct));

        error.Kind.ShouldBe(GitFailureKind.RemoteAlreadyExists);
        error.ExitCode.ShouldBe(3);
    }

    [Fact]
    public async Task Ic_ice_ad_RemoteNameConflict_olarak_siniflandiriliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "ic", "https://example.com/a.git");

        GitException error = await Should.ThrowAsync<GitException>(() => harness.Writer.AddAsync(
            harness.Path,
            new RemoteAddOptions { Name = "ic/main", Url = "https://example.com/b.git" },
            Ct));

        error.Kind.ShouldBe(GitFailureKind.RemoteNameConflict);
    }

    [Fact]
    public async Task Olmayan_remote_RemoteNotFound()
    {
        using Harness harness = await CreateAsync();

        GitException error = await Should.ThrowAsync<GitException>(
            () => harness.Writer.RemoveAsync(harness.Path, "yok", Ct));

        error.Kind.ShouldBe(GitFailureKind.RemoteNotFound);
    }

    [Fact]
    public async Task Silme_plani_silinen_yapilandirmanin_TAMAMINI_tasiyor()
    {
        using Harness harness = await CreateAsync();
        using TestRepository upstream = CreateUpstream(out string url);

        harness.Repository.Git("remote", "add", "origin", url);
        harness.Repository.Git("push", "-u", "origin", "HEAD:main");
        harness.Repository.Git("fetch", "origin");
        harness.Repository.Git("config", "remote.pushDefault", "origin");

        RemoteRemovalPlan plan = await harness.Writer.RemoveAsync(harness.Path, "origin", Ct);

        plan.Remote.Url.ShouldBe(url);
        plan.IsPushDefault.ShouldBeTrue();
        plan.TrackingBranches.ShouldContain("origin/main");
        plan.AffectedBranches.ShouldNotBeEmpty();

        // Counter-evidence: after the removal NONE of this information can be read any more.
        harness.Repository.Git("remote").Trim().ShouldBeEmpty();
        harness.Repository.Git("for-each-ref", "refs/remotes").Trim().ShouldBeEmpty();
        harness.Repository.TryGit("config", "--get", "remote.pushDefault").ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task Silme_plani_SILMEDEN_ONCE_hesaplaniyor()
    {
        // 🔴 SABOTAGE TEST: if the plan is computed after the removal it comes back empty — the
        // information is gone. The remote counterpart of the "read the hash before deleting" rule
        // from P06-T03.
        using Harness harness = await CreateAsync();
        using TestRepository upstream = CreateUpstream(out string url);

        harness.Repository.Git("remote", "add", "origin", url);
        harness.Repository.Git("push", "-u", "origin", "HEAD:main");

        RemoteRemovalPlan plan = await harness.Writer.RemoveAsync(harness.Path, "origin", Ct);

        plan.RecoveryCommands.ShouldNotBeEmpty();
        plan.RecoveryCommands[0].ShouldStartWith("git remote add origin");
        plan.RecoveryCommands.ShouldContain(command => command.StartsWith("git fetch origin", StringComparison.Ordinal));
        plan.RecoveryCommands.ShouldContain(
            command => command.StartsWith("git branch --set-upstream-to=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Kurtarma_komutlari_GERCEKTEN_calisiyor()
    {
        // End to end: do the commands we hand out restore the repository to its previous state
        // by copy-paste?
        using Harness harness = await CreateAsync();
        using TestRepository upstream = CreateUpstream(out string url);

        harness.Repository.Git("remote", "add", "origin", url);
        harness.Repository.Git("remote", "set-url", "--push", "origin", url);
        harness.Repository.Git("push", "-u", "origin", "HEAD:main");
        harness.Repository.Git("fetch", "origin");

        RemoteRemovalPlan plan = await harness.Writer.RemoveAsync(harness.Path, "origin", Ct);

        foreach (string command in plan.RecoveryCommands)
        {
            harness.Repository.Git([.. SplitCommand(command)]);
        }

        GitRemote restored = (await harness.Reader.FindAsync(harness.Path, "origin", Ct))!;
        restored.Url.ShouldBe(url);
        restored.HasSeparatePushUrl.ShouldBeTrue();

        harness.Repository.Git("for-each-ref", "--format=%(refname)", "refs/remotes")
            .ShouldContain("refs/remotes/origin/main");
        harness.Repository.Git("config", "--get", "branch.main.remote").Trim().ShouldBe("origin");
    }

    [Fact]
    public async Task Yeniden_adlandirma_varsayilan_olmayan_refspec_te_UYARI_veriyor()
    {
        // 🔴 EXIT CODE 0 but the job is half done: git does not update the refspec. An interface
        // that looks only at rc would say "renamed successfully".
        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", "https://example.com/a.git");
        harness.Repository.Git("config", "remote.origin.fetch", "+refs/heads/main:refs/remotes/ozel/main");

        RemoteRenameResult result = await harness.Writer.RenameAsync(harness.Path, "origin", "yeni", Ct);

        result.Warnings.ShouldNotBeEmpty();
        result.Warnings[0].ShouldContain("non-default fetch refspec");

        // And it really has not been updated:
        harness.Repository.Git("config", "--get", "remote.yeni.fetch").Trim()
            .ShouldBe("+refs/heads/main:refs/remotes/ozel/main");
    }

    [Fact]
    public async Task Yeniden_adlandirma_temiz_durumda_UYARISIZ()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", "https://example.com/a.git");

        RemoteRenameResult result = await harness.Writer.RenameAsync(harness.Path, "origin", "yeni", Ct);

        result.Warnings.ShouldBeEmpty();
        harness.Repository.Git("config", "--get", "remote.yeni.fetch").Trim()
            .ShouldBe("+refs/heads/*:refs/remotes/yeni/*");
    }

    [Fact]
    public async Task Yeniden_adlandirma_upstream_i_TASIYOR()
    {
        using Harness harness = await CreateAsync();
        using TestRepository upstream = CreateUpstream(out string url);

        harness.Repository.Git("remote", "add", "origin", url);
        harness.Repository.Git("push", "-u", "origin", "HEAD:main");

        await harness.Writer.RenameAsync(harness.Path, "origin", "uzak", Ct);

        harness.Repository.Git("config", "--get", "branch.main.remote").Trim().ShouldBe("uzak");
        harness.Repository.Git("for-each-ref", "--format=%(refname)", "refs/remotes")
            .ShouldContain("refs/remotes/uzak/main");
    }

    [Fact]
    public async Task URL_degistirme_fetch_ve_push_u_ayri_yaziyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", "https://example.com/a.git");

        await harness.Writer.SetUrlAsync(
            harness.Path, "origin", RemoteUrlKind.Fetch, "https://example.com/yeni.git", Ct);
        await harness.Writer.SetUrlAsync(
            harness.Path, "origin", RemoteUrlKind.Push, "ssh://git@example.com/yeni.git", Ct);

        GitRemote remote = (await harness.Reader.FindAsync(harness.Path, "origin", Ct))!;
        remote.Url.ShouldBe("https://example.com/yeni.git");
        remote.PushUrls.ShouldBe(["ssh://git@example.com/yeni.git"]);
    }

    [Fact]
    public async Task COKLU_url_de_tek_adimli_degistirme_REDDEDILIYOR()
    {
        // MEASURED: in this case git says "has multiple values" and stops with 128. Instead of
        // taking the error from git we stop here, so the interface can ask "which URL?".
        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", "https://example.com/bir.git");
        harness.Repository.Git("remote", "set-url", "--add", "origin", "https://example.com/iki.git");

        await Should.ThrowAsync<InvalidOperationException>(() => harness.Writer.SetUrlAsync(
            harness.Path, "origin", RemoteUrlKind.Fetch, "https://example.com/uc.git", Ct));

        // The repository must be unchanged.
        GitRemote remote = (await harness.Reader.FindAsync(harness.Path, "origin", Ct))!;
        remote.FetchUrls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task URL_ekleme_ve_kaldirma()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "origin", "https://example.com/bir.git");

        await harness.Writer.AddUrlAsync(
            harness.Path, "origin", RemoteUrlKind.Fetch, "https://example.com/iki.git", Ct);

        (await harness.Reader.FindAsync(harness.Path, "origin", Ct))!.FetchUrls.Count.ShouldBe(2);

        await harness.Writer.RemoveUrlAsync(
            harness.Path, "origin", RemoteUrlKind.Fetch, "https://example.com/iki.git", Ct);

        (await harness.Reader.FindAsync(harness.Path, "origin", Ct))!.FetchUrls
            .ShouldBe(["https://example.com/bir.git"]);
    }

    [Fact]
    public async Task Tire_ile_baslayan_MEVCUT_remote_ile_calisilabiliyor()
    {
        // Our own verification does not PRODUCE such a name, but one may already exist in the
        // repository; without the `--` separator git mistook it for a flag (rc=129).
        using Harness harness = await CreateAsync();
        harness.Repository.Git("remote", "add", "--", "-eski", "https://example.com/a.git");

        RemoteRemovalPlan plan = await harness.Writer.RemoveAsync(harness.Path, "-eski", Ct);

        plan.Remote.Name.ShouldBe("-eski");
        harness.Repository.Git("remote").Trim().ShouldBeEmpty();
    }

    /// <summary>
    /// Splits the recovery command into arguments (for tests only; single quotes supported).
    /// </summary>
    private static IReadOnlyList<string> SplitCommand(string command)
    {
        List<string> parts = [];
        System.Text.StringBuilder current = new();
        bool inQuotes = false;

        foreach (char c in command)
        {
            switch (c)
            {
                case '\'':
                    inQuotes = !inQuotes;
                    break;
                case ' ' when !inQuotes:
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }

                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        // The first part is "git" — the fixture already runs git itself.
        return [.. parts.Skip(1)];
    }
}
