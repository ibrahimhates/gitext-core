using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The <b>modeless</b> window showing a git command's full output (P05-T07).
/// </summary>
public partial class GitOutputWindow : Window
{
    public GitOutputWindow()
    {
        InitializeComponent();
    }

    /// <summary>Opens the output <b>modeless</b> above its owner.</summary>
    internal static void Open(GitOutputViewModel viewModel, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        new GitOutputWindow { DataContext = viewModel }.ShowOwnedBy(owner);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
