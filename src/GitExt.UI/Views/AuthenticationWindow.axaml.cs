using Avalonia.Controls;
using GitExt.Core;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Kimlik doğrulama penceresi (P06-T09).
/// </summary>
public partial class AuthenticationWindow : Window
{
    public AuthenticationWindow()
    {
        InitializeComponent();
    }

    internal static async Task<GitCredentials?> ShowAsync(AuthenticationViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        AuthenticationWindow window = new() { DataContext = model };

        void Close(object? sender, EventArgs e) => window.Close();

        model.Completed += Close;

        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            model.Completed -= Close;
        }

        return model.Result;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
