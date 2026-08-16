using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>Stash penceresi (P07-T13).</summary>
public partial class StashWindow : Window
{
    public StashWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    internal static async Task ShowAsync(StashViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        StashWindow window = new() { DataContext = model };

        await window.ShowDialog(owner);
    }


    /// <remarks>
    /// The branch name is not bound directly: <see cref="StashViewModel.BranchName"/> is a plain
    /// property and the command's enabled state depends on it; the text change is forwarded here so it
    /// is re-evaluated on every keystroke.
    /// </remarks>
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (this.FindControl<TextBox>("BranchNameBox") is { } box
            && DataContext is StashViewModel model)
        {
            box.TextChanged += (_, _) =>
            {
                model.BranchName = box.Text ?? string.Empty;
                model.BranchCommand.NotifyCanExecuteChanged();
            };
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
