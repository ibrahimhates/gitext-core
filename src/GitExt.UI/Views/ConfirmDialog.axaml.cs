using Avalonia.Controls;

namespace GitExt.UI.Views;

/// <summary>How a confirmation was answered.</summary>
public enum ConfirmAnswer
{
    /// <summary>Backed out of the operation altogether.</summary>
    Cancel,

    /// <summary>Answered no.</summary>
    No,

    /// <summary>Answered yes.</summary>
    Yes,
}

/// <summary>The answer, plus whether the question should be asked again.</summary>
public readonly record struct ConfirmResult(ConfirmAnswer Answer, bool DoNotAskAgain)
{
    public bool Confirmed => Answer == ConfirmAnswer.Yes;
}

/// <summary>
/// A yes/no question with the answer defaulting to <b>no</b> (P12-T03).
/// </summary>
/// <remarks>
/// GitExtensions' <c>MessageBoxes.Confirm</c> and <c>ConfirmSuppressible</c>. The suppression tick
/// and the third button are off unless asked for, so a plain question looks exactly as it did.
/// </remarks>
public partial class ConfirmDialog : Window
{
    private ConfirmAnswer _answer = ConfirmAnswer.No;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    internal static async Task<bool> ShowAsync(string caption, string question, Window owner)
    {
        ConfirmResult result = await AskAsync(caption, question, owner).ConfigureAwait(true);

        return result.Confirmed;
    }

    /// <summary>
    /// Asks the question and reports the answer along with the suppression tick.
    /// </summary>
    /// <param name="caption">The window title.</param>
    /// <param name="question">The question itself.</param>
    /// <param name="owner">The window this one is modal to.</param>
    /// <param name="suppressible">Show "Don't show me this message again".</param>
    /// <param name="withCancel">
    /// Offer a third button. Without it, closing the window is a "no"; with it, the two are
    /// different answers — GitExtensions asks the tracking-reference question with
    /// <c>YesNoCancel</c> because "push without a tracking reference" and "do not push" are not
    /// the same thing.
    /// </param>
    internal static async Task<ConfirmResult> AskAsync(
        string caption,
        string question,
        Window owner,
        bool suppressible = false,
        bool withCancel = false)
    {
        ArgumentNullException.ThrowIfNull(owner);

        ConfirmDialog dialog = new();
        dialog.Apply(caption, question);
        dialog.SuppressBox.IsVisible = suppressible;
        dialog.CancelButton.IsVisible = withCancel;

        if (withCancel)
        {
            dialog.NoButton.IsCancel = false;
            dialog._answer = ConfirmAnswer.Cancel;
        }

        await dialog.ShowDialog(owner);

        return new ConfirmResult(dialog._answer, dialog.SuppressBox.IsChecked == true);
    }

    internal void Apply(string caption, string question)
    {
        Title = caption;
        QuestionText.Text = question;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _answer = ConfirmAnswer.Cancel;
        Close();
    }

    private void OnNoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _answer = ConfirmAnswer.No;
        Close();
    }

    private void OnYesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _answer = ConfirmAnswer.Yes;
        Close();
    }
}
