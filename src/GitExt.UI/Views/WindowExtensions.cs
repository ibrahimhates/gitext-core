using Avalonia.Controls;

namespace GitExt.UI.Views;

/// <summary>
/// Window opening helpers.
/// </summary>
internal static class WindowExtensions
{
    /// <summary>
    /// Opens the window <b>modeless</b>, above its owner when there is one.
    /// </summary>
    /// <remarks>
    /// <c>Show()</c> — not <c>ShowDialog</c>. The comparison window (P04-T16) and the git output window
    /// (P05-T07) share the same reasoning: the user must be able to keep the content open and carry on
    /// working in the main window. With no owner (headless tests) it opens without one.
    /// </remarks>
    internal static void ShowOwnedBy(this Window window, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (owner is null)
        {
            window.Show();
        }
        else
        {
            window.Show(owner);
        }
    }
}
