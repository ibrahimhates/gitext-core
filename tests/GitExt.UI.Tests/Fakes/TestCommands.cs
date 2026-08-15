using GitExt.UI.Commands;

namespace GitExt.UI.Tests;

/// <summary>
/// The command registry used by the tests.
/// </summary>
/// <remarks>
/// It uses the <b>real scheme</b>, not a fake one: the value of the shortcut tests lies exactly in
/// the question "does the key the user will press do the right job"; done with a fake scheme they
/// would only exercise the dispatch mechanism.
/// </remarks>
public static class TestCommands
{
    public static CommandRegistry Registry() =>
        new(new InMemorySettingsStore(), DefaultCommandScheme.Definitions);
}
