using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
