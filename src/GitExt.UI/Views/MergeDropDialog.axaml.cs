using Avalonia.Controls;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Sürükle-bırak birleştirmesinin onayı (P06-T15).
/// </summary>
public partial class MergeDropDialog : Window
{
    private bool _confirmed;

    public MergeDropDialog()
    {
        InitializeComponent();
    }

    internal static async Task<bool> ShowAsync(MergeDropRequest request, Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        MergeDropDialog dialog = new();

        dialog.QuestionText.Text =
            $"Merge branch \"{request.Source}\" into \"{request.Target}\"?";
        dialog.CommandBox.Text = request.Command;

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
