using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// "Go to commit…" (P14-T05) — GitExtensions' <c>FormGoToCommit</c>.
/// </summary>
/// <remarks>
/// GitExtensions closes the form first and only then says <i>No revision found.</i> in a message
/// box. Here the answer is given <b>in the dialog</b>: it is the same message, but a typo does not
/// cost the user the dialog and everything they typed into it.
/// </remarks>
public partial class GoToCommitDialog : Window
{
    private Func<string?, bool> _canResolve = _ => true;
    private string? _expression;

    public GoToCommitDialog()
    {
        InitializeComponent();

        ExpressionTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                ValidationText.IsVisible = false;
            }
        };

        Loaded += (_, _) => ExpressionTextBox.Focus();
    }

    /// <summary>Shows the dialog and returns the accepted expression.</summary>
    /// <remarks>
    /// <c>canResolve</c> answers whether the expression names a commit that is loaded. It is asked
    /// on <i>Go</i>, not per keystroke: a short prefix is a scan over every row.
    /// </remarks>
    internal static async Task<string?> ShowAsync(Func<string?, bool> canResolve, Window owner)
    {
        ArgumentNullException.ThrowIfNull(canResolve);
        ArgumentNullException.ThrowIfNull(owner);

        GoToCommitDialog dialog = new();
        dialog.Apply(canResolve);

        await dialog.ShowDialog(owner);

        return dialog._expression;
    }

    internal void Apply(Func<string?, bool> canResolve)
    {
        ArgumentNullException.ThrowIfNull(canResolve);

        _canResolve = canResolve;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnGoClick(object? sender, RoutedEventArgs e)
    {
        string expression = (ExpressionTextBox.Text ?? string.Empty).Trim();

        if (expression.Length == 0)
        {
            return;
        }

        if (!_canResolve(expression))
        {
            ValidationText.Text = Loc.T("go_to_commit_dialog.axaml.no_revision_found");
            ValidationText.IsVisible = true;
            return;
        }

        _expression = expression;

        Close();
    }
}
