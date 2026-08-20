namespace GitExt.UI.ViewModels;

/// <summary>
/// The side that shows the dashboard's small dialogs (P12-T03).
/// </summary>
/// <remarks>
/// The same reasoning as <see cref="ICreateBranchPrompt"/>: a dialog needs an owner window and that
/// is only known at the moment it opens, so the ViewModel does not get to know about
/// <c>Window</c>. Its counterpart in GitExtensions is <c>FormDashboardCategoryTitle</c> plus the
/// two <c>MessageBoxes.Show</c> questions in <c>UserRepositoriesList</c>.
/// </remarks>
public interface IDashboardPrompt
{
    /// <summary>
    /// Asks for a category name.
    /// </summary>
    /// <param name="existingCategories">
    /// The names already in use — the dialog refuses these, as GitExtensions does.
    /// </param>
    /// <param name="currentName">The name being renamed, or <see langword="null"/> when adding.</param>
    /// <returns>The name, or <see langword="null"/> when the user cancelled.</returns>
    Task<string?> AskCategoryNameAsync(IReadOnlyList<string> existingCategories, string? currentName);

    /// <summary>
    /// Asks a yes/no question before something that cannot be undone.
    /// </summary>
    /// <remarks>
    /// Deleting a category and clearing the recent list both throw away the user's own filing;
    /// GitExtensions asks for both, with <b>No</b> as the default button.
    /// </remarks>
    Task<bool> ConfirmAsync(string caption, string question);
}
