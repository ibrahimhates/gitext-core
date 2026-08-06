using Avalonia.Controls;

namespace GitExt.UI.Views;

/// <summary>
/// Süren birleştirmenin iptal onayı (P06-T12).
/// </summary>
public partial class AbortMergeDialog : Window
{
    private bool _confirmed;

    public AbortMergeDialog()
    {
        InitializeComponent();
    }

    internal static async Task<bool> ShowAsync(IReadOnlyList<string> conflicted, Window owner)
    {
        ArgumentNullException.ThrowIfNull(conflicted);
        ArgumentNullException.ThrowIfNull(owner);

        AbortMergeDialog dialog = new();

        dialog.ConflictList.ItemsSource = conflicted;

        // Liste boşsa panel de gösterilmiyor: boş bir başlık kullanıcıya bir şey söylemez.
        dialog.ConflictPanel.IsVisible = conflicted.Count > 0;

        await dialog.ShowDialog(owner);

        return dialog._confirmed;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }
}
