using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The modeless window comparing two revisions (P04-T16).
/// </summary>
/// <remarks>
/// Opened with <see cref="Window.Show()"/> rather than <c>ShowDialog</c>: the user has to be able to
/// open several comparisons at once and put them side by side. That was the need the embedded panel
/// could not meet.
/// </remarks>
public partial class CompareWindow : Window
{
    public CompareWindow()
    {
        InitializeComponent();

        RefreshButton.Click += OnRefreshClick;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private CompareViewModel? Model => DataContext as CompareViewModel;

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => Refresh();

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F5:
                Refresh();
                e.Handled = true;
                break;

            // The window is modeless; closing it has to be in the user's hands.
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Refresh()
    {
        if (Model is { } model)
        {
            _ = model.RefreshAsync();
        }
    }
}
