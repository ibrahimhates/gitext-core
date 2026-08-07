using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P08-T15 — <c>git config</c> yazma, <b>gerçek git'e karşı</b>.
/// </summary>
/// <remarks>
/// Testler yalnızca yerel kapsamı kullanıyor: global kapsam kullanıcının <c>~/.gitconfig</c>
/// dosyasına yazardı ve bir test <b>asla</b> geliştiricinin gerçek yapılandırmasını
/// değiştirmemeli.
/// </remarks>
public class GitConfigWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(GitConfigWriter Writer, GitConfigReader Reader)> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return (new GitConfigWriter(new GitWriter(runner, queue), runner), new GitConfigReader(runner));
    }

    [Fact]
    public async Task Yerel_ayar_yazilip_okunuyor()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, GitConfigReader reader) = await CreateAsync();

        await writer.SetAsync(repository.Path, "user.name", "Ayşe Yılmaz", GitConfigScope.Local, Ct);

        (await reader.GetAsync(repository.Path, "user.name", Ct)).ShouldBe("Ayşe Yılmaz");
        (await writer.GetScopedAsync(repository.Path, "user.name", GitConfigScope.Local, Ct))
            .ShouldBe("Ayşe Yılmaz");
    }

    /// <summary>
    /// 🔴 Boş değer ayarı <b>siliyor</b>, boşa ayarlamıyor.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: <c>git config user.name ""</c> çıkış kodu 0 veriyor ve ayar <b>var ama
    /// boş</b> oluyor. Boş bir <c>user.name</c> ile commit atmak, hiç ayarlanmamış
    /// olmasından farklı ve daha kötü bir hata üretir. Bu test, alanı temizleyen
    /// kullanıcının "sil" dediğini koruyor.
    /// </remarks>
    [Fact]
    public async Task Bos_deger_ayari_SILIYOR()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, GitConfigReader reader) = await CreateAsync();

        await writer.SetAsync(repository.Path, "gitext.probe", "değer", GitConfigScope.Local, Ct);
        await writer.SetAsync(repository.Path, "gitext.probe", "", GitConfigScope.Local, Ct);

        (await reader.GetAsync(repository.Path, "gitext.probe", Ct)).ShouldBeNull();
        (await writer.GetScopedAsync(repository.Path, "gitext.probe", GitConfigScope.Local, Ct))
            .ShouldBeNull("ayar silinmeli, boş dizeye ayarlanmamalı");
    }

    /// <summary>
    /// 🔴 Olmayan bir ayarı silmek <b>hata değil</b>.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: <c>git config --unset</c> olmayan anahtarda <b>çıkış kodu 5</b> veriyor —
    /// 0 da 1 de değil. Hata sayılsaydı zaten boş olan bir alanı temizleyen kullanıcı,
    /// hiçbir şey yanlış gitmemişken hata görürdü.
    /// </remarks>
    [Fact]
    public async Task Olmayan_ayari_silmek_hata_degil()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, _) = await CreateAsync();

        await Should.NotThrowAsync(() =>
            writer.SetAsync(repository.Path, "gitext.hicYokBu", "", GitConfigScope.Local, Ct));
    }

    /// <summary>
    /// Kapsamlı okuma birleşimi değil, <b>o dosyayı</b> okuyor.
    /// </summary>
    /// <remarks>
    /// Ayrım şart: birleşik okuma, değerin hangi dosyadan geldiğini söylemiyor. Global bir
    /// değeri yerel alanda göstermek, kullanıcının kaydettiğinde farkında olmadan yerel bir
    /// kopya oluşturması demekti.
    /// </remarks>
    [Fact]
    public async Task Kapsamli_okuma_birlesimi_degil_o_dosyayi_okuyor()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, GitConfigReader reader) = await CreateAsync();

        // Fixture yerel `user.email` ayarlıyor; onu kaldırıp yerel kapsamın gerçekten
        // boş olduğunu görüyoruz.
        await writer.SetAsync(repository.Path, "user.email", "", GitConfigScope.Local, Ct);

        (await writer.GetScopedAsync(repository.Path, "user.email", GitConfigScope.Local, Ct))
            .ShouldBeNull();

        // Birleşik okuma yine bir değer bulabilir (geliştiricinin global ayarı); bu test
        // onun ne olduğuna değil, YEREL kapsamın ayrı okunduğuna bakıyor.
        await writer.SetAsync(repository.Path, "user.email", "yerel@örnek", GitConfigScope.Local, Ct);

        (await reader.GetAsync(repository.Path, "user.email", Ct)).ShouldBe("yerel@örnek");
    }

    [Fact]
    public async Task Ayarlanmamis_anahtar_null_donuyor()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, _) = await CreateAsync();

        (await writer.GetScopedAsync(repository.Path, "gitext.yok", GitConfigScope.Local, Ct))
            .ShouldBeNull();
    }

    /// <summary>
    /// 🔴 Depo olmayan bir dizinde yerel okuma <b>çökmüyor</b>.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: <c>git config --local</c> depo dışında <c>fatal</c> ve çıkış kodu <b>128</b>
    /// veriyor. Komut satırından verilen dizin depo olmayabilir; bunun için istisna atmak
    /// uygulamayı açılmaz hâle getirirdi.
    /// </remarks>
    [Fact]
    public async Task Depo_olmayan_dizinde_yerel_okuma_cokmuyor()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gitext-nonrepo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);

        try
        {
            (GitConfigWriter writer, _) = await CreateAsync();

            (await writer.GetScopedAsync(directory, "user.name", GitConfigScope.Local, Ct))
                .ShouldBeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
