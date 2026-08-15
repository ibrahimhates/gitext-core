using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using GitExt.UI.Localization;
using GitExt.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace GitExt.UI.Tests;

/// <summary>
/// Headless test uygulaması.
/// </summary>
/// <remarks>
/// <c>UseHeadlessDrawing = false</c> ile gerçek Skia render'ı devreye girer; bu sayede
/// <c>CaptureRenderedFrame()</c> gerçek pikseller üretir ve metin çiziminin doğruluğu
/// doğrulanabilir (P01-T16).
/// </remarks>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());

        // Uygulamanın gerçek teması (P08-T07). Eklenmezse testler paletsiz çalışır ve
        // `DynamicResource` bulamadığında SESSİZCE değer atamaz — yani tema kaynaklarının
        // yanlış olduğu bir kod da yeşil test verirdi.
        Styles.Add(new StyleInclude(new Uri("avares://GitExt.UI/Themes/GitExtTheme.axaml"))
        {
            Source = new Uri("avares://GitExt.UI/Themes/GitExtTheme.axaml"),
        });

        // Varsayılan açık tema — uygulamayla aynı (kullanıcı kararı, 2026-07-29).
        RequestedThemeVariant = ThemeVariant.Light;

        // Çevirmen (P11-T02). Uygulamada bunu composition root yapıyor; testlerde
        // yapılmazsa {loc:Translate ...} bağlamaları anahtar ADINI döndürüyor ve
        // yerleşim testleri "main.repository" gibi değerler görüyor.
        //
        // Varsayılan İngilizce: testlerin beklediği metinler de İngilizce.
        TranslateExtension.Attach(new Translator(new InMemorySettingsStore()));
    }
}
