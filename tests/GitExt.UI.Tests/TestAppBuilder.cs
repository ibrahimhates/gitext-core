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
/// Headless test application.
/// </summary>
/// <remarks>
/// With <c>UseHeadlessDrawing = false</c> real Skia rendering kicks in; that way
/// <c>CaptureRenderedFrame()</c> produces real pixels and the correctness of text rendering can be
/// verified (P01-T16).
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

        // The application's real theme (P08-T07). If it is not added the tests run without a palette
        // and when `DynamicResource` cannot be found it SILENTLY assigns no value — meaning code with
        // wrong theme resources would give a green test too.
        Styles.Add(new StyleInclude(new Uri("avares://GitExt.UI/Themes/GitExtTheme.axaml"))
        {
            Source = new Uri("avares://GitExt.UI/Themes/GitExtTheme.axaml"),
        });

        // Default light theme — same as the application (user decision, 2026-07-29).
        RequestedThemeVariant = ThemeVariant.Light;

        // Translator (P11-T02). In the application the composition root does this; if it is not done
        // in the tests the {loc:Translate ...} bindings return the NAME of the key and the layout
        // tests see values like "main.repository".
        //
        // Default English: the texts the tests expect are English as well.
        Translator translator = new(new InMemorySettingsStore());
        TranslateExtension.Attach(translator);
        Loc.Attach(translator);
    }
}
