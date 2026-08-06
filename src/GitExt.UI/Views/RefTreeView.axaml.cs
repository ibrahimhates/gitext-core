using Avalonia;
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

    /// <summary>Sürükle-bırak birleştirmesini üstlenen taraf (P06-T15).</summary>
    internal Func<string, string, Task>? MergeDropped { get; set; }

    /// <summary>Sürüklenen dalın adı; sürükleme yokken boş.</summary>
    private string _dragged = string.Empty;

    /// <summary>Sürüklemenin başlaması için gereken en küçük hareket.</summary>
    /// <remarks>
    /// 🔑 Eşik olmadan tek bir tıklamadaki minik titreme bile sürükleme sayılırdı — planın
    /// "kazara sürükleme gerçek bir risk" maddesi. Onay diyaloğu ikinci savunma hattı,
    /// bu ilki.
    /// </remarks>
    private const double DragThreshold = 6;

    private Point _pressedAt;

    /// <remarks>
    /// ⚠️ Avalonia'nın <c>DoDragDropAsync</c>'i <b>basma</b> olayının argümanını istiyor
    /// (ölçüldü: <c>PointerEventArgs</c> kabul etmiyor). Eşiği hareket olayında ölçüp
    /// sürüklemeyi saklanan basma argümanıyla başlatmak bu yüzden gerekli.
    /// </remarks>
    private PointerPressedEventArgs? _pressed;

    public RefTreeView()
    {
        InitializeComponent();

        RefTree.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        RefTree.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        RefTree.AddHandler(PointerReleasedEvent, (_, _) => _pressed = null, RoutingStrategies.Tunnel);
        RefTree.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        RefTree.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>Sürükleme verisinin biçimi — yalnızca bu görünümün ürettiği veri kabul edilir.</summary>
    internal static DataFormat<string> BranchFormat { get; } = DataFormat.CreateStringApplicationFormat("gitext-branch");

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = e;
        _pressedAt = e.GetPosition(this);
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } pressed
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || Selected is not { IsCheckoutable: true } node)
        {
            return;
        }

        Point position = e.GetPosition(this);

        if (Math.Abs(position.X - _pressedAt.X) < DragThreshold
            && Math.Abs(position.Y - _pressedAt.Y) < DragThreshold)
        {
            return;
        }

        _pressed = null;
        _dragged = node.FullName;

        DataTransfer data = new();
        data.Add(DataTransferItem.Create(BranchFormat, node.FullName));

        await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Yalnızca bu görünümden gelen dal verisi kabul ediliyor; dosya sürüklemesi
        // ana pencerenin işi (depo açma) ve buraya karışmamalı.
        e.DragEffects = e.DataTransfer.Contains(BranchFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(BranchFormat)
            || MergeDropped is not { } merge
            || Selected is not { Kind: RefNodeKind.LocalBranch } target)
        {
            return;
        }

        string source = e.DataTransfer.TryGetValue(BranchFormat) ?? _dragged;

        if (source.Length > 0 && !string.Equals(source, target.FullName, StringComparison.Ordinal))
        {
            await merge(source, target.FullName);
        }
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
