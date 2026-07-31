using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// İki revizyonu karşılaştıran modeless pencere (P04-T16).
/// </summary>
/// <remarks>
/// <see cref="Window.Show()"/> ile açılır, <c>ShowDialog</c> ile değil: kullanıcı aynı anda
/// birkaç karşılaştırma açıp yan yana koyabilmeli. Bu, gömülü panelin çözemediği ihtiyaçtı.
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

            // Pencere modeless; kapatma kullanıcının elinde olmalı.
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
