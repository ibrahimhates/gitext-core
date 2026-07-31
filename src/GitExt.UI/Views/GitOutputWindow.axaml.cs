using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Git komutunun tam çıktısını gösteren <b>modeless</b> pencere (P05-T07).
/// </summary>
public partial class GitOutputWindow : Window
{
    public GitOutputWindow()
    {
        InitializeComponent();
    }

    /// <summary>Çıktıyı sahibinin üstünde <b>modeless</b> açar.</summary>
    internal static void Open(GitOutputViewModel viewModel, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        new GitOutputWindow { DataContext = viewModel }.ShowOwnedBy(owner);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
