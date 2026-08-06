using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Dal paneli (P06-T13) ve bağlam menüsü (P06-T14).
/// </summary>
/// <remarks>
/// Çift tıklama ve menü öğeleri burada karşılanıyor çünkü seçilen düğümün ne olduğuna
/// bakmak gerekiyor: klasör ya da başlık üzerinde bir şey yapılmamalı, yoksa kullanıcı
/// ağacı gezerken kazara dal değiştirirdi.
/// </remarks>
public partial class RefTreeView : UserControl
{
    /// <summary>Checkout isteğini üstlenen taraf.</summary>
    internal Func<string, Task>? Checkout { get; set; }

    /// <summary>Ana penceredeki komutları çağıran taraf.</summary>
    internal MainWindowViewModel? Commands { get; set; }

    public RefTreeView()
    {
        InitializeComponent();
    }

    private RefNodeViewModel? Selected =>
        (DataContext as RefTreeViewModel)?.Selected;

    private async void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Selected is { IsCheckoutable: true } node && Checkout is { } checkout)
        {
            await checkout(node.FullName);
        }
    }

    private async void OnCheckoutClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { IsCheckoutable: true } node && Checkout is { } checkout)
        {
            await checkout(node.FullName);
        }
    }

    private async void OnCreateBranchClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.CreateBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnMergeClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.MergeCommand.ExecuteAsync(null);
        }
    }

    private async void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.RenameBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.DeleteBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnPushClick(object? sender, RoutedEventArgs e)
    {
        if (Commands is { } model)
        {
            await model.PushCommand.ExecuteAsync(null);
        }
    }

    private async void OnCopyNameClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { FullName.Length: > 0 } node
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(node.FullName);
        }
    }
}
