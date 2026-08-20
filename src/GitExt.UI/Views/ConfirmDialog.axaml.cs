using Avalonia.Controls;

namespace GitExt.UI.Views;

/// <summary>
/// A yes/no question with the answer defaulting to <b>no</b> (P12-T03).
/// </summary>
public partial class ConfirmDialog : Window
{
    private bool _confirmed;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    internal static async Task<bool> ShowAsync(string caption, string question, Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        ConfirmDialog dialog = new();
        dialog.Apply(caption, question);

        await dialog.ShowDialog(owner);

        return dialog._confirmed;
    }

    internal void Apply(string caption, string question)
    {
        Title = caption;
        QuestionText.Text = question;
    }

    private void OnNoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnYesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }
}
