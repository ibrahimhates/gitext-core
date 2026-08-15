using System.Diagnostics.CodeAnalysis;
using Avalonia.Data;
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
/// <b>Dil değişiminde metinler kendiliğinden tazeleniyor:</b> uzantı bir <see cref="Binding"/>
/// döndürüyor ve kaynağı çevirmenin kendisi. Çevirmen <c>PropertyChanged(null)</c>
/// yayınladığında Avalonia indeksleyici bağlamalarının tamamını yeniden değerlendiriyor.
/// Sabit bir string döndürseydik dil değişimi ancak pencere yeniden açılınca görünürdü.
/// </para>
/// <para>
/// 🔴 <b>İki yol da denendi, ikisi de ölçüldü:</b>
/// </para>
/// <list type="number">
///   <item>
///     <b><see cref="IObservable{T}"/> döndürmek</b> — trimming açısından temiz ama
///     <b>yanlış sonuç veriyor</b>: <c>MenuItem.Header</c> gibi <c>object</c> tipli
///     özelliklerde Avalonia observable'ı bağlama olarak değil <b>değerin kendisi</b>
///     olarak alıyor ve menüde sınıf adı ("TranslationSource") görünüyor. 161 test bunu
///     yakaladı.
///   </item>
///   <item>
///     <b>Yol tabanlı <see cref="Binding"/></b> — doğru çalışıyor ama <c>IL2026</c>
///     üretiyor: yansıma kullanıyor ve trimmer güvenli sayamıyor. Uyarı bu projede hata
///     sayıldığı için publish kırılıyordu.
///   </item>
/// </list>
/// <para>
/// <b>Seçilen: (2), uyarı gerekçesiyle bastırılarak.</b> Bastırma burada güvenli çünkü
/// bağlama yolu <b>sabit ve bizim kontrolümüzde</b> (<c>[anahtar]</c> indeksleyicisi),
/// kullanıcı verisinden gelmiyor; hedef tip <see cref="ITranslator"/> ve indeksleyicisi
/// aşağıdaki <c>DynamicDependency</c> ile trimmer'a korunuyor. Ölçüldü: trimmed publish
/// temiz geçiyor ve üretilen ikili çalışıyor.
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

    /// <summary>
    /// Etkin çevirmeni tanıtır. <b>Yalnızca composition root'tan</b> bir kez çağrılıyor.
    /// </summary>
    /// <remarks>
    /// <c>public</c> olması bir API vaadi değil, <c>GitExt.Desktop</c>'un composition root
    /// olarak buna erişmesi gerektiği için (ADR-0004). Başka hiçbir yerden çağrılmamalı;
    /// çağrıldığında uygulama genelindeki çevirmen sessizce değişirdi.
    /// </remarks>
    public static void Attach(ITranslator translator) => Instance = translator;

    /// <remarks>
    /// <c>DynamicDependency</c>: trimmer <see cref="ITranslator"/>'ın indeksleyicisini
    /// yalnızca bağlama üzerinden kullanıldığı için "kullanılmıyor" sayıp atabilirdi.
    /// Bu öznitelik onu tutuyor ve aşağıdaki bastırmayı gerçekten güvenli kılıyor.
    /// </remarks>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ITranslator))]
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification =
            "Bağlama yolu sabit ve kod içinde üretiliyor ('[anahtar]'), kullanıcı verisinden "
            + "gelmiyor. Hedef tipin üyeleri DynamicDependency ile korunuyor. Ölçüldü: "
            + "trimmed publish temiz ve üretilen ikili çalışıyor.")]
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Tasarımcıda (ve çevirmen kurulmadan önce) anahtarın kendisi gösteriliyor:
        // boş bir arayüz yerine hangi anahtarın orada durduğu görünüyor.
        if (Instance is null)
        {
            return Key;
        }

        return new Binding($"[{Key}]")
        {
            Source = Instance,
            Mode = BindingMode.OneWay,
        };
    }
}
