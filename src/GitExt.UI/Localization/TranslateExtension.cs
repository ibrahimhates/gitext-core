using System.Diagnostics.CodeAnalysis;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace GitExt.UI.Localization;

/// <summary>
/// Translated text in XAML: <c>Text="{loc:Translate settings.title}"</c> (P11-T02).
/// </summary>
/// <remarks>
/// <para>
/// The <b>first markup extension</b> in the project. The alternative was opening a property on the
/// ViewModel for every text; across 42 XAML files and around 460 texts, that would have bloated the
/// ViewModels with hundreds of lines that only carry text, and static labels have no business being
/// in a ViewModel.
/// </para>
/// <para>
/// <b>The texts refresh by themselves when the language changes:</b> the extension returns a
/// <see cref="Binding"/> whose source is the translator itself. When the translator raises
/// <c>PropertyChanged(null)</c>, Avalonia re-evaluates every indexer binding. Had we returned a
/// fixed string, a language change would only show after the window was reopened.
/// </para>
/// <para>
/// 🔴 <b>Both routes were tried, and both were measured:</b>
/// </para>
/// <list type="number">
///   <item>
///     <b>Returning an <see cref="IObservable{T}"/></b> — clean as far as trimming goes, but it
///     <b>gives the wrong result</b>: on <c>object</c>-typed properties such as
///     <c>MenuItem.Header</c>, Avalonia takes the observable not as a binding but as <b>the value
///     itself</b>, and the class name ("TranslationSource") shows up in the menu. 161 tests caught
///     this.
///   </item>
///   <item>
///     <b>A path-based <see cref="Binding"/></b> — works correctly but produces <c>IL2026</c>: it
///     uses reflection and the trimmer cannot consider it safe. Because warnings are errors in this
///     project, the publish was breaking.
///   </item>
/// </list>
/// <para>
/// <b>Chosen: (2), with the warning suppressed and the reason recorded.</b> The suppression is safe
/// here because the binding path is <b>fixed and under our control</b> (the <c>[key]</c> indexer)
/// and does not come from user data; the target type is <see cref="ITranslator"/> and its indexer is
/// protected from the trimmer by the <c>DynamicDependency</c> below. Measured: the trimmed publish
/// goes through clean and the binary it produces works.
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
    /// The translator in force across the application.
    /// </summary>
    /// <remarks>
    /// 🔴 Its being static is NOT a Service Locator but a technical necessity: markup extension
    /// instances are created by the XAML resolver, not by the DI container — there is no way to pass
    /// a dependency to the constructor. The composition root (ADR-0004) is still the sole authority:
    /// <c>Translator</c> is built there and handed in here <b>once</b>.
    /// </remarks>
    internal static ITranslator? Instance { get; private set; }

    /// <summary>The key to translate.</summary>
    public string Key { get; set; } = "";

    public TranslateExtension()
    {
    }

    /// <summary>Positional use in XAML: <c>{loc:Translate settings.title}</c>.</summary>
    public TranslateExtension(string key) => Key = key;

    /// <summary>
    /// Registers the translator in force. Called once <b>from the composition root only</b>.
    /// </summary>
    /// <remarks>
    /// Its being <c>public</c> is not an API promise, it is because <c>GitExt.Desktop</c> needs to
    /// reach it as the composition root (ADR-0004). It must not be called from anywhere else; doing
    /// so would silently change the application-wide translator.
    /// </remarks>
    public static void Attach(ITranslator translator) => Instance = translator;

    /// <remarks>
    /// <c>DynamicDependency</c>: the trimmer could count <see cref="ITranslator"/>'s indexer as
    /// "unused" and drop it, because it is only used through a binding. This attribute keeps it, and
    /// is what makes the suppression below genuinely safe.
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
        // In the designer (and before the translator is set up) the key itself is shown: rather than
        // an empty UI, you can see which key sits there.
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
