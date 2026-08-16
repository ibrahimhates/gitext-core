using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T05 — remote reading.
/// </summary>
/// <remarks>
/// The weight of these tests is on the cases that the measurement showed make <c>git remote -v</c>
/// and <c>git remote get-url</c> unusable: URLs containing tabs/spaces, multiple URLs, a remote with
/// no URL, a name containing a dot, and <c>insteadOf</c>.
/// </remarks>
public class RemoteReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<RemoteReader> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new RemoteReader(new GitProcessRunner(executable));
    }

    [Fact]
    public async Task Hic_remote_yoksa_bos_liste()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        RemoteReader reader = await CreateAsync();

        IReadOnlyList<GitRemote> remotes = await reader.ReadAllAsync(repository.Path, Ct);

        remotes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fetch_ve_push_url_leri_ayri_okunuyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("remote", "add", "origin", "https://example.com/a.git");
        repository.Git("remote", "set-url", "--push", "origin", "ssh://git@example.com/a.git");

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "origin", Ct))!;

        remote.Url.ShouldBe("https://example.com/a.git");
        remote.HasSeparatePushUrl.ShouldBeTrue();
        remote.EffectivePushUrls.ShouldBe(["ssh://git@example.com/a.git"]);
    }

    [Fact]
    public async Task Ayri_push_url_yoksa_push_fetch_e_gidiyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("remote", "add", "origin", "https://example.com/a.git");

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "origin", Ct))!;

        // In this case `git remote -v` REPEATS the fetch URL on the (push) line; the answer to
        // "is there a separate push URL" is not there, it is in the config.
        remote.HasSeparatePushUrl.ShouldBeFalse();
        remote.EffectivePushUrls.ShouldBe(["https://example.com/a.git"]);
    }

    [Fact]
    public async Task SEKME_iceren_url_dogru_okunuyor()
    {
        // 🔴 `git remote -v` makes this line unparsable: the separator is a tab too.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        const string url = "https://a\tb/c.git";
        repository.Git("config", "remote.sekmeli.url", url);
        repository.Git("config", "remote.sekmeli.fetch", "+refs/heads/*:refs/remotes/sekmeli/*");

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "sekmeli", Ct))!;

        remote.Url.ShouldBe(url);
    }

    [Fact]
    public async Task BOSLUKLU_url_dogru_okunuyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        const string url = "/tmp/bos luk/depo.git";
        repository.Git("remote", "add", "yerel", url);

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "yerel", Ct))!;

        remote.Url.ShouldBe(url);
    }

    [Fact]
    public async Task SATIR_SONU_iceren_url_dogru_okunuyor()
    {
        // 🔴 Without `-z`, `git config --get-regexp` splits this value across TWO LINES (measured);
        // a line-based parser would mistake the second piece for a separate record.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        const string url = "https://a\nb/c.git";
        repository.Git("config", "remote.satirli.url", url);
        repository.Git("config", "remote.satirli.fetch", "+refs/heads/*:refs/remotes/satirli/*");

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "satirli", Ct))!;

        remote.Url.ShouldBe(url);
    }

    [Fact]
    public async Task COKLU_url_hepsi_okunuyor()
    {
        // Here `git remote -v` gives three lines for a SINGLE remote; a parser assuming two lines
        // per name would shift the next remote.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("remote", "add", "origin", "https://example.com/bir.git");
        repository.Git("remote", "set-url", "--add", "origin", "https://example.com/iki.git");

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "origin", Ct))!;

        remote.FetchUrls.ShouldBe(["https://example.com/bir.git", "https://example.com/iki.git"]);

        // Fetch uses the first one; this is the "primary URL".
        remote.Url.ShouldBe("https://example.com/bir.git");
    }

    [Fact]
    public async Task URL_siz_remote_listede_kaliyor_ve_URL_i_NULL()
    {
        // 🔴 MEASURED: for this remote `git remote get-url` prints THE NAME ITSELF with exit code
        // 0, while `git remote -v` leaves it blank. Neither of them is used.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("config", "remote.hayalet.fetch", "+refs/heads/*:refs/remotes/hayalet/*");

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "hayalet", Ct))!;

        remote.Url.ShouldBeNull();
        remote.FetchUrls.ShouldBeEmpty();

        repository.Git("remote", "get-url", "hayalet").Trim().ShouldBe("hayalet");
    }

    [Fact]
    public async Task NOKTALI_ad_dogru_ayristiriliyor()
    {
        // 🔴 Reading the `remote.a.b.url` key with `Split('.')[1]` would take the name to be "a".
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("remote", "add", "a.b", "https://example.com/ab.git");

        RemoteReader reader = await CreateAsync();
        IReadOnlyList<GitRemote> remotes = await reader.ReadAllAsync(repository.Path, Ct);

        remotes.Select(r => r.Name).ShouldBe(["a.b"]);
        remotes[0].Url.ShouldBe("https://example.com/ab.git");
    }

    [Fact]
    public async Task insteadOf_tanimliyken_HAM_config_degeri_okunuyor()
    {
        // 🔴 The reason this test exists: `get-url`/`remote -v` give back the rewritten URL.
        // If the interface puts that in an edit box and saves it, the shortcut is destroyed
        // permanently.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("config", "url.https://example.com/.insteadOf", "ornek:");
        repository.Git("remote", "add", "kisa", "ornek:proje.git");

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "kisa", Ct))!;

        remote.Url.ShouldBe("ornek:proje.git");

        // Counter-evidence: git's own channel really does answer differently.
        repository.Git("remote", "get-url", "kisa").Trim()
            .ShouldBe("https://example.com/proje.git");
    }

    [Fact]
    public async Task Buyuk_kucuk_harf_ayri_remote()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("remote", "add", "Buyuk", "https://example.com/b.git");
        repository.Git("remote", "add", "buyuk", "https://example.com/k.git");

        RemoteReader reader = await CreateAsync();

        (await reader.FindAsync(repository.Path, "Buyuk", Ct))!.Url
            .ShouldBe("https://example.com/b.git");
        (await reader.FindAsync(repository.Path, "buyuk", Ct))!.Url
            .ShouldBe("https://example.com/k.git");
    }

    [Fact]
    public async Task Varsayilan_olmayan_refspec_isaretleniyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("remote", "add", "origin", "https://example.com/a.git");

        RemoteReader reader = await CreateAsync();
        (await reader.FindAsync(repository.Path, "origin", Ct))!.HasDefaultFetchRefspec
            .ShouldBeTrue();

        repository.Git("config", "remote.origin.fetch", "+refs/heads/main:refs/remotes/ozel/main");

        (await reader.FindAsync(repository.Path, "origin", Ct))!.HasDefaultFetchRefspec
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Olmayan_remote_icin_null()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        RemoteReader reader = await CreateAsync();

        (await reader.FindAsync(repository.Path, "yok", Ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData("https://ali:s3cr3t@example.com/x.git", "https://ali:***@example.com/x.git")]
    [InlineData("https://ali@example.com/x.git", "https://ali@example.com/x.git")]
    [InlineData("https://example.com/x.git", "https://example.com/x.git")]
    [InlineData("git@example.com:ali/x.git", "git@example.com:ali/x.git")]
    [InlineData("", "")]
    public void Parola_maskeleme(string url, string expected) =>
        GitRemote.MaskCredentials(url).ShouldBe(expected);
}
