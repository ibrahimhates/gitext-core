using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T05 — uzak depo okuma.
/// </summary>
/// <remarks>
/// Testlerin ağırlığı, ölçümde <c>git remote -v</c>'yi ve <c>git remote get-url</c>'ü
/// kullanılamaz kılan durumlarda: sekmeli/boşluklu URL, çoklu URL, URL'siz remote,
/// noktalı ad ve <c>insteadOf</c>.
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

        // `git remote -v` bu durumda (push) satırında fetch URL'sini TEKRARLIYOR; "ayrı push
        // URL'si var mı" sorusunun cevabı orada yok, config'te var.
        remote.HasSeparatePushUrl.ShouldBeFalse();
        remote.EffectivePushUrls.ShouldBe(["https://example.com/a.git"]);
    }

    [Fact]
    public async Task SEKME_iceren_url_dogru_okunuyor()
    {
        // 🔴 `git remote -v` bu satırı ayrıştırılamaz yapıyor: ayraç da sekme.
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
        // 🔴 `-z` olmadan `git config --get-regexp` bu değeri İKİ SATIRA bölüyor (ölçüldü);
        // satır tabanlı ayrıştırıcı ikinci parçayı ayrı bir kayıt sanardı.
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
        // `git remote -v` burada TEK bir remote için üç satır veriyor; ad başına iki satır
        // varsayan ayrıştırıcı sonraki remote'u kaydırırdı.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("remote", "add", "origin", "https://example.com/bir.git");
        repository.Git("remote", "set-url", "--add", "origin", "https://example.com/iki.git");

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "origin", Ct))!;

        remote.FetchUrls.ShouldBe(["https://example.com/bir.git", "https://example.com/iki.git"]);

        // Fetch ilkini kullanır; "birincil URL" bu.
        remote.Url.ShouldBe("https://example.com/bir.git");
    }

    [Fact]
    public async Task URL_siz_remote_listede_kaliyor_ve_URL_i_NULL()
    {
        // 🔴 ÖLÇÜLDÜ: bu remote için `git remote get-url` çıkış kodu 0 ile ADIN KENDİSİNİ
        // basıyor, `git remote -v` ise boş bırakıyor. İkisi de kullanılmıyor.
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
        // 🔴 `remote.a.b.url` anahtarını `Split('.')[1]` ile okumak adı "a" sanardı.
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
        // 🔴 Bu testin varlık sebebi: `get-url`/`remote -v` yeniden yazılmış URL'yi veriyor.
        // Arayüz onu düzenleme kutusuna koyup kaydederse kısayol kalıcı olarak yok oluyor.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Git("config", "url.https://example.com/.insteadOf", "ornek:");
        repository.Git("remote", "add", "kisa", "ornek:proje.git");

        RemoteReader reader = await CreateAsync();
        GitRemote remote = (await reader.FindAsync(repository.Path, "kisa", Ct))!;

        remote.Url.ShouldBe("ornek:proje.git");

        // Karşı kanıt: git'in kendi kanalı gerçekten farklı cevap veriyor.
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
