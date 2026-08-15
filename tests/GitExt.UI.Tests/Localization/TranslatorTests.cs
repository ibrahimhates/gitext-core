using System.ComponentModel;
using GitExt.UI.Localization;

namespace GitExt.UI.Tests.Localization;

/// <summary>
/// Çeviri altyapısının davranışını doğrular (P11-T01).
/// </summary>
/// <remarks>
/// Buradaki testlerin kapattığı boşluk, yerelleştirmenin <b>sessizce</b> yanlış çalışmasıdır:
/// eksik bir anahtar boş etiket üretir, keşfedilmeyen bir dil listede hiç görünmez, dil
/// değişimi arayüzü tazelemezse kullanıcı hiçbir şey olmadığını sanır. Hiçbiri istisna
/// fırlatmaz.
/// </remarks>
public class TranslatorTests
{
    private static Translator Build() => new(new InMemorySettingsStore());

    [Fact]
    public void Gomulu_dil_dosyalari_kesfediliyor()
    {
        // Diller koda yazılmış bir listeden değil, gömülü kaynaklardan geliyor.
        // Bu test kırılırsa ya joker girdi (.csproj) ya da kaynak adı öneki bozulmuştur.
        Translator translator = Build();

        translator.Available.Select(l => l.Code).ShouldContain("en");
        translator.Available.Select(l => l.Code).ShouldContain("tr");
    }

    [Fact]
    public void Dilin_adi_kendi_dilinde_gosteriliyor()
    {
        // "Turkish" değil "Türkçe": dil listesini okuyan kişi, o dili ARAYAN kişidir.
        Translator translator = Build();

        translator.Available.First(l => l.Code == "tr").Name.ShouldBe("Türkçe");
        translator.Available.First(l => l.Code == "en").Name.ShouldBe("English");
    }

    [Fact]
    public void Varsayilan_dil_ingilizce()
    {
        Build().Current.ShouldBe("en");
    }

    [Fact]
    public void Dil_degistirilebiliyor()
    {
        Translator translator = Build();

        translator.Use("tr");

        translator.Current.ShouldBe("tr");

        // Türkçe karşılık: dil gerçekten değişti mi, yoksa yalnızca kod mu değişti?
        translator["settings.settings"].ShouldBe("Ayarlar");
    }

    [Fact]
    public void Dil_tercihi_ayarlara_yaziliyor()
    {
        InMemorySettingsStore settings = new();
        Translator translator = new(settings);

        translator.Use("tr");

        settings.Current.General.Language.ShouldBe("tr");
    }

    [Fact]
    public void Dil_degisince_tum_baglamalar_tazeleniyor()
    {
        // PropertyChanged(null) = "bu nesnede her şey değişti". Avalonia bunu indeksleyici
        // bağlamalarının tamamını yeniden değerlendirmek olarak okuyor. Bu yayın olmazsa
        // dil değişimi ancak pencere yeniden açılınca görünürdü.
        Translator translator = Build();
        List<string?> changed = [];
        ((INotifyPropertyChanged)translator).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        translator.Use("tr");

        changed.ShouldContain((string?)null);
    }

    [Fact]
    public void Ayni_dile_gecmek_bildirim_uretmiyor()
    {
        // Gereksiz tazeleme, her ayar kaydında tüm arayüzü yeniden değerlendirmek demek.
        Translator translator = Build();
        translator.Use("tr");

        int count = 0;
        ((INotifyPropertyChanged)translator).PropertyChanged += (_, _) => count++;

        translator.Use("tr");

        count.ShouldBe(0);
    }

    [Fact]
    public void Taninmayan_dil_yok_sayiliyor()
    {
        // Var olmayan bir dile geçmek, arayüzü anahtar adlarıyla dolu bırakmamalı.
        Translator translator = Build();

        translator.Use("klingon");

        translator.Current.ShouldBe("en");
    }

    [Fact]
    public void Eksik_anahtar_anahtarin_kendisini_donduruyor()
    {
        // Boş string döndürmek en kötü seçenek: arayüzde boş bir etiket belirir ve
        // eksikliğin nereden geldiği anlaşılmaz. Anahtar görününce hem eksiklik gözle
        // fark ediliyor hem de hangi anahtarın ekleneceği okunuyor.
        Build()["boyle.bir.anahtar.yok"].ShouldBe("boyle.bir.anahtar.yok");
    }

    [Fact]
    public void Bos_anahtar_cokmuyor()
    {
        Build()[""].ShouldBe(string.Empty);
    }

    [Fact]
    public void Format_yer_tutuculari_dolduruyor()
    {
        // Gerçek bir yer tutuculu anahtar kullanılıyor: yedek İngilizce artık dosyadan
        // değil koddan geliyor (P11-T10), uydurma bir "en" katalogu enjekte edilemiyor.
        Translator translator = new(new InMemorySettingsStore());

        translator.Format("commit_list.commits_loaded", 42).ShouldBe("42 commits loaded…");
    }

    [Fact]
    public void Bozuk_yer_tutucu_cokmuyor()
    {
        // Çeviride bozuk bir "{0" uygulamayı çökertmemeli: ham şablon gösteriliyor,
        // hata görünür kalıyor ama arayüz ayakta. Bozuk şablon bir ÇEVİRİDEN gelebilir,
        // o yüzden etkin dil üzerinden kuruluyor.
        Translator translator = Translator.ForTesting(
            new InMemorySettingsStore(),
            new Dictionary<string, Func<Stream>>
            {
                ["GitExt.UI.Locales.tr.json"] = () => new MemoryStream(
                    System.Text.Encoding.UTF8.GetBytes(
                        """{"_meta":{"code":"tr","name":"Türkçe"},"commit_list.commits_loaded":"{0 bozuk"}""")),
            });

        translator.Use("tr");

        Should.NotThrow(() => translator.Format("commit_list.commits_loaded", 1));
    }

    [Fact]
    public void Kayitli_tercih_baslangicta_uygulaniyor()
    {
        InMemorySettingsStore settings = new();
        settings.Current.General.Language = "tr";

        Translator translator = new(settings);
        translator.ApplyStored();

        translator.Current.ShouldBe("tr");
    }

    [Fact]
    public void Tercih_yoksa_ingilizce_kaliyor()
    {
        // InvariantGlobalization=true altında CurrentUICulture boş geliyor (ölçüldü,
        // P11-T00), yani sistem dili tespiti çalışmıyor ve İngilizceye düşülüyor.
        InMemorySettingsStore settings = new();

        Translator translator = new(settings);
        translator.ApplyStored();

        translator.Current.ShouldBe("en");
    }
}
