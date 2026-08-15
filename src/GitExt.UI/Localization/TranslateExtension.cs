using System.ComponentModel;
using Avalonia.Markup.Xaml;

namespace GitExt.UI.Localization;

/// <summary>
/// XAML'de çevrilmiş metin: <c>Text="{loc:Translate settings.title}"</c> (P11-T02).
/// </summary>
/// <remarks>
/// <para>
/// Projedeki <b>ilk markup extension</b>. Alternatif olarak her metin için ViewModel'de bir
/// özellik açmak vardı; 42 XAML dosyasında ~460 metin için bu, ViewModel'leri yalnızca metin
/// taşıyan yüzlerce satırla şişirirdi ve statik etiketlerin ViewModel'de işi yok.
/// </para>
/// <para>
/// <b>Dil değişiminde metinler kendiliğinden tazeleniyor:</b> uzantı bir
/// <see cref="IObservable{T}"/> döndürüyor; çevirmen <c>PropertyChanged(null)</c> yayınladığında
/// akış yeni metni itiyor. Sabit bir string döndürseydik dil değişimi ancak pencere yeniden
/// açılınca görünürdü.
/// </para>
/// <para>
/// 🔴 <b>Neden <c>Binding</c> değil de observable:</b> ilk yazımda <c>new Binding($"[{Key}]")</c>
/// kullanıldı — normal derlemede sorunsuz çalıştı, ama <c>PublishTrimmed=true</c> ile publish
/// <b>IL2026 ile kırıldı</b>: yol tabanlı <c>Binding</c> yansıma kullanıyor ve trimmer onu
/// güvenli sayamıyor. Hata yalnızca gerçek bir trimmed publish denemesinde ortaya çıktı
/// (aynı sınıf tuzak bu projede daha önce ayarlar ve renk paleti tarafında da yaşandı).
/// Observable yolu yansıma içermiyor.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;TextBlock Text="{loc:Translate settings.theme}" /&gt;
/// &lt;TabItem Header="{loc:Translate settings.tab.appearance}" /&gt;
/// </code>
/// </example>
public sealed class TranslateExtension : MarkupExtension
{
    /// <summary>
    /// Uygulama genelinde etkin çevirmen.
    /// </summary>
    /// <remarks>
    /// 🔴 Statik olması bir Service Locator DEĞİL, teknik bir zorunluluk: markup extension
    /// örneklerini XAML çözümleyici yaratıyor, DI kapsayıcısı değil — yapıcıya bağımlılık
    /// geçirmenin bir yolu yok. Composition root (ADR-0004) yine tek yetkili: <c>Translator</c>
    /// orada kuruluyor ve buraya <b>bir kez</b> veriliyor.
    /// </remarks>
    internal static ITranslator? Instance { get; private set; }

    /// <summary>Çevrilecek anahtar.</summary>
    public string Key { get; set; } = "";

    public TranslateExtension()
    {
    }

    /// <summary>XAML'de konumsal kullanım: <c>{loc:Translate settings.title}</c>.</summary>
    public TranslateExtension(string key) => Key = key;

    /// <summary>Etkin çevirmeni tanıtır. Composition root'tan bir kez çağrılıyor.</summary>
    internal static void Attach(ITranslator translator) => Instance = translator;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Tasarımcıda (ve çevirmen kurulmadan önce) anahtarın kendisi gösteriliyor:
        // boş bir arayüz yerine hangi anahtarın orada durduğu görünüyor.
        if (Instance is null)
        {
            return Key;
        }

        return new TranslationSource(Instance, Key);
    }

    /// <summary>
    /// Tek bir anahtarın güncel metnini yayan akış.
    /// </summary>
    /// <remarks>
    /// Abone olunduğunda mevcut metni hemen veriyor, sonra her dil değişiminde yenisini.
    /// Aboneliğin bırakılması olay kaydını da kaldırıyor — pencere kapandığında çevirmen
    /// kapalı pencereleri canlı tutmuyor.
    /// </remarks>
    private sealed class TranslationSource(ITranslator translator, string key) : IObservable<object?>
    {
        public IDisposable Subscribe(IObserver<object?> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            void OnChanged(object? sender, PropertyChangedEventArgs e) => observer.OnNext(translator[key]);

            translator.PropertyChanged += OnChanged;
            observer.OnNext(translator[key]);

            return new Subscription(() => translator.PropertyChanged -= OnChanged);
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            private Action? _dispose = dispose;

            public void Dispose()
            {
                Action? action = Interlocked.Exchange(ref _dispose, null);
                action?.Invoke();
            }
        }
    }
}
