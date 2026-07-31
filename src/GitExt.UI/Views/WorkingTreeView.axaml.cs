using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Çalışma dizini görünümü (P05-T09).
/// </summary>
/// <remarks>
/// <para>
/// Klavye kısayolları: <c>Space</c> seçili dosyayı karşı tarafa taşır (stage/unstage),
/// <c>Ctrl+Space</c> tümünü, <c>F5</c> yeniler.
/// </para>
/// <para>
/// ⚠️ <b>Odak, bu ekranın en kırılgan yeri.</b> Her stage/unstage işleminden sonra iki liste
/// de yeniden kuruluyor; odaklı <c>ListBoxItem</c> yok oluyor ve <b>sonraki tuş olayı
/// görünümün ağacından hiç geçmiyor</b> — klavye sessizce ölüyor (P04-T12'de ölçüldü).
/// Odağı geri vermek tek başına yetmiyor: konteyner o an <b>henüz oluşmamış</b> oluyor ve
/// <c>ContainerFromIndex</c> <see langword="null"/> dönüyor, bu yüzden
/// <see cref="DispatcherPriority.Loaded"/> ile erteleniyor.
/// </para>
/// </remarks>
public partial class WorkingTreeView : UserControl
{
    public WorkingTreeView()
    {
        InitializeComponent();

        // Tünelleme: içteki `ScrollViewer` kabaran tuş olayını yutuyor (Faz 03'te ölçüldü).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private WorkingTreeViewModel? ViewModel => DataContext as WorkingTreeViewModel;

    /// <summary>Kullanıcının en son dokunduğu liste.</summary>
    private ListBox ActiveList =>
        ViewModel?.IsStagedListActive == true ? StagedList : UnstagedList;

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        // Hiçbir şeye odak yoksa tuş olayları bu ağaçtan HİÇ geçmez ve kısayollar sessizce
        // çalışmaz (Faz 03'te ölçüldü).
        FocusActiveList();
    }

    /// <summary>
    /// Odaklanan liste "etkin" olur; diff onu izler.
    /// </summary>
    /// <remarks>
    /// İki listede aynı anda seçim durabiliyor. Etkin listeyi odağa bağlamak, kullanıcının
    /// baktığı yerle gösterilen diff'i aynı tutuyor — aksi halde staged listesinde
    /// gezerken unstaged'in diff'ine bakılırdı.
    /// </remarks>
    private void OnUnstagedFocused(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } model)
        {
            model.IsStagedListActive = false;
        }
    }

    private void OnStagedFocused(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } model)
        {
            model.IsStagedListActive = true;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } model)
        {
            return;
        }

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Space when control:
                Run(model.IsStagedListActive ? model.UnstageAllAsync() : model.StageAllAsync());
                e.Handled = true;
                break;

            case Key.Space:
                Run(model.IsStagedListActive
                    ? model.UnstageSelectedAsync()
                    : model.StageSelectedAsync());
                e.Handled = true;
                break;

            case Key.F5:
                Run(model.RefreshAsync());
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void OnStageClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.StageSelectedAsync());

    private void OnStageAllClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.StageAllAsync());

    private void OnUnstageClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.UnstageSelectedAsync());

    private void OnUnstageAllClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.UnstageAllAsync());

    /// <summary>
    /// İşlemi çalıştırır ve <b>bittikten sonra</b> odağı listeye geri verir.
    /// </summary>
    private void Run(Task? operation)
    {
        if (operation is null)
        {
            return;
        }

        _ = RunCoreAsync(operation);
    }

    private async Task RunCoreAsync(Task operation)
    {
        await operation.ConfigureAwait(true);

        FocusActiveList();
    }

    /// <summary>
    /// Etkin listedeki seçili satıra odaklanır.
    /// </summary>
    /// <remarks>
    /// Konteyner henüz oluşmamış olabilir (liste yeni kuruldu); o durumda
    /// <see cref="DispatcherPriority.Loaded"/> ile erteleniyor. İlk deneme sessizce
    /// başarısız oluyordu.
    /// </remarks>
    private void FocusActiveList()
    {
        if (TryFocusActiveList())
        {
            return;
        }

        Dispatcher.UIThread.Post(() => TryFocusActiveList(), DispatcherPriority.Loaded);
    }

    private bool TryFocusActiveList()
    {
        ListBox list = ActiveList;

        if (list.SelectedIndex < 0)
        {
            // Seçim yoksa liste kutusunun kendisi odaklanamaz (`ListBox.Focusable` false);
            // görünümün kendisine odaklanmak kısayolları yine de çalışır kılıyor.
            return Focus();
        }

        if (list.ContainerFromIndex(list.SelectedIndex) is not Control container)
        {
            return false;
        }

        list.ScrollIntoView(list.SelectedIndex);

        return container.Focus();
    }
}
