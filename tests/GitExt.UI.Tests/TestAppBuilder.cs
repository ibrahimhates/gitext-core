using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
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
    }
}
