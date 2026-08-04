using System.Globalization;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExt.Core.Model;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Commit listesi görünümü. Klavye gezinmesi burada bağlanır (P03-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden kod arkasında?</b> Buradaki iş saf görünüm işi: hangi tuşun ne yaptığı, bir
/// sayfanın kaç satır ettiği ve odağın nereye gideceği. Sayfa boyutu <c>ScrollViewer</c>'ın
/// görünür alanından gelir — ViewModel'ın bilmediği ve bilmemesi gereken bir şey. Gezinme
/// <b>kararı</b> ise ViewModel'da (<see cref="CommitListViewModel.MoveSelection"/> vb.) ve
/// orada ayrıca test ediliyor.
/// </para>
/// <para>
/// <b>ÖLÇÜLEN ÜÇ DAVRANIŞ</b> (hepsi kodu bu hale getirdi):
/// </para>
/// <list type="number">
/// <item><c>ListBox</c> <c>↑↓</c>, <c>Home</c> ve <c>End</c> ile seçimi kendisi taşıyor;
/// <c>PgUp</c>/<c>PgDn</c>'de ise yalnızca <c>ScrollViewer</c> kayıyor, <b>seçim yerinde
/// kalıyor</b>. Sayfa gezinmesi bu yüzden elle uygulanıyor.</item>
/// <item><c>ScrollViewer</c> <c>ListBox</c>'ın <b>içinde</b> olduğu için kabaran (bubble)
/// olayı önce alıp <c>Handled</c> işaretliyor. Bu yüzden tuşlar <b>tünelleme</b>
/// (<see cref="RoutingStrategies.Tunnel"/>) fazında yakalanıyor — aksi halde
/// <c>PgUp</c>/<c>PgDn</c> hiç görülmezdi.</item>
/// <item><c>ListBox.Focusable</c> <see langword="false"/>'tur; odaklanabilen şey
/// <c>ListBoxItem</c>'dır ve ok tuşu gezinmesi <b>odaklanmış konteyneri</b> temel alır,
/// <c>SelectedIndex</c>'i değil.</item>
/// </list>
/// </remarks>
public partial class CommitListView : UserControl
{
    /// <summary>
    /// Görünür alan hesaplanamazsa kullanılan yedek sayfa boyutu.
    /// </summary>
    /// <remarks>
    /// Yalnızca liste henüz ölçülmemişken (ilk kare) devreye girer. Hiçbir şey yapmamaktansa
    /// makul bir sayfa kadar hareket etmek yeğdir.
    /// </remarks>
    private const int FallbackPageSize = 20;

    /// <summary>Satırlar gelince odağı listeye devretmeyi bekliyor muyuz?</summary>
    private bool _awaitingFirstRows;

    private CommitListViewModel? _watched;

    public CommitListView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        // Tünelleme şart: kabarma fazında ScrollViewer sayfa tuşlarını yutuyor (ölçüldü).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private CommitListViewModel? ViewModel => DataContext as CommitListViewModel;

    /// <summary>
    /// Görünüm yüklendiğinde klavyeyi kullanılabilir hale getirir.
    /// </summary>
    /// <remarks>
    /// Odak hiçbir şeyde değilse tuş olayları bu görünümün ağacından <b>hiç geçmez</b> ve
    /// kısayollar sessizce çalışmaz. Kullanıcının önce fareyle tıklamak zorunda kalmaması için
    /// açılışta odak verilir.
    /// </remarks>
    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (FocusSelectedRow())
        {
            return;
        }

        // Satırlar henüz gelmedi — depo yüklemesi görünümden sonra bitiyor. Geçici olarak
        // arama kutusuna odaklanılıyor ki tuşlar bir yere gitsin, ama satırlar gelince odak
        // listeye devredilecek (aksi halde açılışta ok tuşları arama kutusuna yazardı).
        _awaitingFirstRows = true;
        ShaSearchBox.Focus();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_watched is not null)
        {
            _watched.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _watched = ViewModel;

        if (_watched is not null)
        {
            _watched.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_awaitingFirstRows || e.PropertyName != nameof(CommitListViewModel.SelectedIndex))
        {
            return;
        }

        // Kullanıcı bu arada arama kutusuna yazmaya başladıysa odağı elinden almıyoruz.
        if (!string.IsNullOrEmpty(ShaSearchBox.Text))
        {
            _awaitingFirstRows = false;
            return;
        }

        // Yerleşimden SONRA denenmeli: satırlar yeni eklendiği için ListBoxItem konteyneri
        // henüz oluşmamış olur ve odaklanacak bir şey bulunamaz.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_awaitingFirstRows && FocusSelectedRow())
                {
                    _awaitingFirstRows = false;
                }
            },
            DispatcherPriority.Loaded);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        CommitListViewModel? viewModel = ViewModel;

        if (viewModel is null)
        {
            return;
        }

        if (e.Key is Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            FocusSearch();
            ShaSearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        // Ctrl+D: seçili commit'leri karşılaştır (P04-T16).
        if (e.Key is Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OpenComparison(viewModel, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
            return;
        }

        // Arama kutusunda yazarken gezinme tuşları kutuya ait: PgDn ile metin içinde
        // gezinmek isteyen kullanıcının listesi kaymamalı.
        if (ShaSearchBox.IsFocused)
        {
            HandleSearchKey(viewModel, e);
            return;
        }

        HandleNavigationKey(viewModel, e);
    }

    private void HandleSearchKey(CommitListViewModel viewModel, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                viewModel.ApplySearch();

                // Odak arama kutusunda kalır — kullanıcı aramaya devam edebilsin.
                ScrollSelectionIntoView();
                e.Handled = true;
                break;

            case Key.Escape:
                // Aramadan listeye dönüş: kullanıcı gezinmeye devam edebilsin.
                FocusSelectedRow();
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void HandleNavigationKey(CommitListViewModel viewModel, KeyEventArgs e)
    {
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        bool moved = e.Key switch
        {
            Key.PageDown => viewModel.MoveSelection(PageSize()),
            Key.PageUp => viewModel.MoveSelection(-PageSize()),

            // Alt+↓ daha eskiye (ebeveyn), Alt+↑ daha yeniye (çocuk) gider.
            // Yön ekrandakiyle aynı: eski commit'ler aşağıda.
            Key.Down when alt => viewModel.GoToParent(),
            Key.Up when alt => viewModel.GoToChild(),

            _ => false,
        };

        // Hedef bulunamasa bile tuş tüketilir: aksi halde ScrollViewer görünümü seçimden
        // koparıp kaydırır, ya da Alt+↓ ListBox'a düşüp seçimi bir satır aşağı kaydırır —
        // kullanıcı "ebeveyne git" isterken sessizce başka bir commit'e düşer.
        bool ours = e.Key is Key.PageDown or Key.PageUp
            || (alt && e.Key is Key.Down or Key.Up);

        if (!ours)
        {
            return;
        }

        e.Handled = true;

        if (moved)
        {
            FocusSelectedRow();
        }
    }

    /// <summary>Arama kutusuna odaklanır (<c>Ctrl+F</c>).</summary>
    public void FocusSearch() => ShaSearchBox.Focus();

    /// <summary>
    /// Bir sayfanın kaç satır ettiğini görünür alandan hesaplar.
    /// </summary>
    /// <remarks>
    /// Sabit satır yüksekliği varsayılmıyor; liste öğesinin gerçek yüksekliği ölçülüyor.
    /// Satır yüksekliği tema veya yazı tipiyle değişebilir.
    /// </remarks>
    private int PageSize()
    {
        ScrollViewer? scroll = CommitList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        double rowHeight = CommitList.GetRealizedContainers().FirstOrDefault()?.Bounds.Height ?? 0;

        if (scroll is null || rowHeight <= 0)
        {
            return FallbackPageSize;
        }

        // Bir satır eksik kaydırmak kasıtlı: kullanıcının baktığı son satır, sayfa
        // değiştikten sonra ekranın ucunda kalır ve bağlam kopmaz.
        int rows = (int)(scroll.Viewport.Height / rowHeight) - 1;

        return Math.Max(1, rows);
    }

    private void ScrollSelectionIntoView()
    {
        int index = ViewModel?.SelectedIndex ?? -1;

        if (index >= 0)
        {
            CommitList.ScrollIntoView(index);
        }
    }

    // ---- Bağlam menüsü (P08-T27) ----

    /// <summary>
    /// Seçili commit'ten metin kopyalar.
    /// </summary>
    /// <remarks>
    /// GitExtensions'ın "Copy to clipboard" alt menüsündeki alanların karşılığı:
    /// hash · mesaj · yazar · tarih · dal adı.
    /// </remarks>
    private async void CopyAsync(Func<CommitRowViewModel, string?> select)
    {
        if (ViewModel?.SelectedRow is not { } row)
        {
            return;
        }

        string? text = select(row);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnCopyHashClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => row.Commit.Id.Value);

    private void OnCopyMessageClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => string.IsNullOrEmpty(row.Commit.Body)
            ? row.Commit.Subject
            : row.Commit.Subject + "\n\n" + row.Commit.Body);

    private void OnCopyAuthorClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => $"{row.Commit.Author.Name} <{row.Commit.Author.Email}>");

    private void OnCopyDateClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => row.Commit.Author.When.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>Satırdaki dal rozetlerinin adlarını kopyalar.</summary>
    private void OnCopyBranchClick(object? sender, RoutedEventArgs e) =>
        CopyAsync(row => string.Join('\n', row.Badges
            .Where(badge => badge.Kind is RefBadgeKind.LocalBranch or RefBadgeKind.RemoteBranch)
            .Select(badge => badge.Text)));

    /// <summary>
    /// "Burada yeni dal oluştur…" (P06-T01).
    /// </summary>
    /// <remarks>
    /// Komut ana pencerenin ViewModel'ında; bu görünüm yalnızca commit listesini tanıyor.
    /// Bağlamayla ulaşmak yerine tepe pencereden okunuyor — bağlam menüsü görsel ağaçta
    /// ayrı bir ad kapsamında ve kapalıyken bağlamaları hiç değerlendirilmiyor (P05-T13'te
    /// ölçüldü). Başlangıç noktası olarak seçili commit ViewModel tarafında okunuyor.
    /// </remarks>
    private async void OnCreateBranchClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.CreateBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnRenameBranchClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.RenameBranchCommand.ExecuteAsync(null);
        }
    }

    private async void OnDeleteBranchClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.DeleteBranchCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// "Dala geç" / "Bu commit'e geç" (P06-T02).
    /// </summary>
    /// <remarks>
    /// İki menü öğesi de aynı komutu çağırıyor: hangisinin olacağını seçili commit'te
    /// yerel bir dal olup olmaması belirliyor ve sonuç <b>diyalogda yazılı</b>.
    /// GitExtensions'ta iki ayrı öğe olduğu için ikisi de yerinde duruyor (§ 9).
    /// </remarks>
    private async void OnCheckoutClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel model)
        {
            await model.CheckoutCommand.ExecuteAsync(null);
        }
    }

    private void OnCompareSelectedClick(object? sender, RoutedEventArgs e) => RequestComparison(againstHead: false);

    private void OnCompareHeadClick(object? sender, RoutedEventArgs e) => RequestComparison(againstHead: true);

    /// <summary>Çalışma ağacıyla karşılaştırma: seçim tek satıra indirgeniyor.</summary>
    private void OnCompareWorkingTreeClick(object? sender, RoutedEventArgs e)
    {
        CommitList.SelectedItems?.Clear();
        RequestComparison(againstHead: false);
    }

    private void RequestComparison(bool againstHead)
    {
        if (ViewModel is { } viewModel)
        {
            OpenComparison(viewModel, againstHead);
        }
    }

    private void OnGoToParentClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.GoToParent() == true)
        {
            FocusSelectedRow();
        }
    }

    private void OnGoToChildClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.GoToChild() == true)
        {
            FocusSelectedRow();
        }
    }

    /// <summary>
    /// Karşılaştırma penceresini açar (P04-T16).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kaynak seçimi: <b>iki satır seçiliyse</b> o ikisi karşılaştırılır (P03-T14'te açılan
    /// çoklu seçimin ilk tüketicisi), tek satır seçiliyse commit ile <b>çalışma ağacı</b>.
    /// <c>Shift</c> basılıysa tek seçimde de commit ↔ <c>HEAD</c> karşılaştırılır.
    /// </para>
    /// <para>
    /// Pencere <b>modeless</b>: <c>Show()</c> ile açılıyor ve aynı anda birden fazla
    /// olabiliyor. Kullanıcının itirazı tam da buydu — tek gömülü panel iki değişikliği
    /// yan yana koymayı imkânsız kılıyordu.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Karşılaştırma istendiğinde, pencere açılmadan hemen önce tetiklenir.
    /// </summary>
    /// <remarks>
    /// Testler için: pencerenin gerçekten açıldığını headless ortamda saymak güvenilir değil,
    /// ama <b>tuşun doğru revizyonlarla doğru komuta bağlandığı</b> buradan doğrulanabiliyor.
    /// </remarks>
    internal event EventHandler<CompareViewModel>? ComparisonRequested;

    private void OpenComparison(CommitListViewModel viewModel, bool againstHead)
    {
        CompareViewModel? compare = viewModel.CreateComparison();

        if (compare is null)
        {
            return;
        }

        List<CommitId> selected = [.. CommitList.SelectedItems?
            .OfType<CommitRowViewModel>()
            .Select(row => row.Commit.Id) ?? []];

        Task loading;

        if (selected.Count >= 2)
        {
            // Liste en yeniden eskiye sıralı; kullanıcı "eskiden yeniye" bir fark bekler.
            loading = compare.CompareAsync(selected[^1], selected[0]);
        }
        else if (viewModel.SelectedRow is { } row)
        {
            loading = againstHead
                ? compare.CompareAsync(row.Commit.Id.Value, "HEAD")
                : compare.CompareWithWorkingTreeAsync(row.Commit.Id.Value);
        }
        else
        {
            return;
        }

        _ = loading;

        ComparisonRequested?.Invoke(this, compare);

        new CompareWindow { DataContext = compare }
            .ShowOwnedBy(TopLevel.GetTopLevel(this) as Window);
    }

    /// <summary>
    /// Seçili satırı görünür yapar ve <b>odağı ona taşır</b>.
    /// </summary>
    /// <remarks>
    /// Odağı taşımak şart: <c>ListBox</c>'ın ok tuşu gezinmesi odaklanmış konteyneri temel
    /// aldığı için (ölçüldü), <c>PgDn</c> ile 40 satır ilerledikten sonra <c>↓</c>'ya
    /// basıldığında seçim <b>eski satırın yanına geri sıçrardı</b>.
    /// </remarks>
    /// <returns>Odaklanacak bir satır bulunduysa <see langword="true"/>.</returns>
    private bool FocusSelectedRow()
    {
        int index = ViewModel?.SelectedIndex ?? -1;

        if (index < 0)
        {
            return false;
        }

        CommitList.ScrollIntoView(index);

        // Sanallaştırma yüzünden konteyner ancak görünür alana girdikten sonra oluşur;
        // ScrollIntoView'dan önce istemek null döndürürdü.
        return CommitList.ContainerFromIndex(index)?.Focus() == true;
    }
}
