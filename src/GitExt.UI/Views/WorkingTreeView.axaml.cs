using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExt.Core;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Çalışma dizini görünümü (P05-T09).
/// </summary>
/// <remarks>
/// <para>
/// Klavye kısayolları artık komut kaydından geliyor (P08-T01/T02): <c>Space</c> seçili
/// dosyayı karşı tarafa taşır, <c>Ctrl+Shift+S</c>/<c>Ctrl+Shift+U</c> tümünü,
/// <c>Ctrl+Enter</c> commit atar. <c>F5</c> artık <b>küresel</b> bir komut.
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

    private ShortcutDispatcher? _dispatcher;

    private ICommandRegistry? _registry;

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

        // 🔴 Metin kutusundayken liste kısayolları ÇALIŞMAMALI. Tünelleme fazında
        // yakaladığımız için `Space` tuşu commit mesajına boşluk yazmak yerine dosya
        // stage'lerdi — mesaj yazan kullanıcı farkında olmadan index'i değiştirirdi.
        // Commit kısayolu bilinçli istisna: commit zaten mesaj kutusundan verilir.
        bool isCommit = _registry?.GetGesture(CommandIds.WorkingTreeCommit) is { } commit
            && commit.Matches(e);

        if (e.Source is TextBox && !isCommit)
        {
            return;
        }

        // F5 artık KÜRESEL (P08-T02): burada da yakalasaydık aynı jest iki bağlamda
        // kayıtlı olurdu ve çakışma tespiti onu haklı olarak raporlardı.
        _dispatcher?.Handle(e);
    }

    /// <summary>Bağlam kısayollarını komut kaydına bağlar (P08-T01).</summary>
    public ShortcutDispatcher AttachShortcuts(ICommandRegistry registry)
    {
        _registry = registry;

        ShortcutDispatcher dispatcher = new(registry, CommandContext.WorkingTree);

        dispatcher.Bind(CommandIds.WorkingTreeToggleStage, () => WithModel(m =>
            m.IsStagedListActive ? m.UnstageSelectedAsync() : m.StageSelectedAsync()));

        dispatcher.Bind(CommandIds.WorkingTreeStageAll, () => WithModel(m => m.StageAllAsync()));
        dispatcher.Bind(CommandIds.WorkingTreeUnstageAll, () => WithModel(m => m.UnstageAllAsync()));

        // 🔴 ÖLÇÜLDÜ (P05-T12): `AcceptsReturn` açık bir `TextBox` Ctrl+Enter'ı da düz Enter
        // gibi işleyip SATIR SONU EKLİYOR. Tünelleme fazında yakalanıp `Handled`
        // işaretlenmezse commit atılırken mesaja bir de boş satır girerdi.
        dispatcher.Bind(CommandIds.WorkingTreeCommit, () => WithModel(m => m.CommitAsync()));

        _dispatcher = dispatcher;

        return dispatcher;
    }

    private bool WithModel(Func<WorkingTreeViewModel, Task> operation)
    {
        if (ViewModel is not { } model)
        {
            return false;
        }

        Run(operation(model));

        return true;
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

    // ---- P05-T12: commit paneli ----

    private void OnCommitClick(object? sender, RoutedEventArgs e) => Run(ViewModel?.CommitAsync());

    // ---- P05-T15: onay ve güvenlik ağı ----

    private void OnResetAllClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.ResetChangesAsync(DiscardScope.All));

    private void OnResetUnstagedClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.ResetChangesAsync(DiscardScope.UnstagedOnly));

    private void OnUndoResetClick(object? sender, RoutedEventArgs e) =>
        Run(ViewModel?.UndoResetAsync());

    private void OnDismissResetNoticeClick(object? sender, RoutedEventArgs e) =>
        ViewModel?.ClearResetNotice();

    // ---- P05-T13: mesaj yardımcıları ----

    /// <summary>
    /// Geçmiş menüsü açılırken mesajları okur ve öğeleri kurar.
    /// </summary>
    /// <remarks>
    /// Öğeler <c>ItemsSource</c> ile bağlanmıyor: <see cref="MenuFlyout"/> içinde sabit
    /// öğeler (<i>yalnızca benim mesajlarım</i> + ayraç) ile veriye bağlı öğeler bir arada
    /// duruyor ve tek bir <c>ItemsSource</c> ikisini birden veremiyor. Sabit öğeler
    /// <b>korunuyor</b>, geçmiş satırları her açılışta yenileniyor.
    /// </remarks>
    private async void OnMessageHistoryOpening(object? sender, EventArgs e)
    {
        if (ViewModel is not { } model)
        {
            return;
        }

        // Flyout içindeki adlar kod arkasında ALAN OLARAK ÜRETİLMİYOR (ayrı ad kapsamı);
        // menüye düğmenin kendisi üzerinden erişiliyor.
        if (MessageHistoryButton.Flyout is not MenuFlyout flyout)
        {
            return;
        }

        await model.Message.LoadRecentAsync().ConfigureAwait(true);

        RebuildHistoryMenu(flyout, model.Message);
    }

    private static void RebuildHistoryMenu(MenuFlyout flyout, CommitMessageViewModel message)
    {
        List<object> items = [.. flyout.Items.OfType<object>()];

        // Ayraçtan sonrası geçmiş; her açılışta baştan kuruluyor.
        int separator = items.FindIndex(item => item is Separator);

        if (separator >= 0 && items.Count > separator + 1)
        {
            items.RemoveRange(separator + 1, items.Count - separator - 1);
        }

        foreach (CommitMessageHistoryItem entry in message.RecentMessages)
        {
            items.Add(new MenuItem
            {
                Header = entry.Label,
                Command = entry.ApplyCommand,
                CommandParameter = entry,

                // Tam mesaj ipucunda: menüde yalnızca ilk satır görünüyor, kullanıcı gövdesi
                // olan bir mesajı seçmeden önce ne aldığını görebilmeli.
                [ToolTip.TipProperty] = entry.Message,
            });
        }

        flyout.Items.Clear();

        foreach (object item in items)
        {
            flyout.Items.Add(item);
        }
    }

    private void OnApplyTemplateClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } model)
        {
            _ = model.Message.ApplyTemplateAsync();
        }
    }
}
