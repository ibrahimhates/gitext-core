using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExt.UI.Commands;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Değişen dosyalar listesi ve diff görünümü (P04-T08…T12).
/// </summary>
/// <remarks>
/// Bağımsız bileşen: ana pencereyi tanımaz, dışarıdan yalnızca <c>DiffViewModel</c> alır.
/// Aynı görünüm <c>P04-T16</c>'daki karşılaştırma penceresinde de kullanılacak.
/// </remarks>
public partial class DiffView : UserControl
{
    public DiffView()
    {
        InitializeComponent();

        // ⚠️ TÜNEL fazı — Faz 03'te ölçüldü: liste içindeki `ScrollViewer` kabaran tuş
        // olayını önce alıp `Handled` işaretliyor, kabarma fazındaki bir işleyici hiç
        // çalışmıyor.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private ShortcutDispatcher? _dispatcher;

    private DiffViewModel? Model => DataContext as DiffViewModel;

    /// <summary>
    /// Bağlam kısayollarını komut kaydına bağlar (P08-T01).
    /// </summary>
    /// <remarks>
    /// Kayıt verilmezse görünüm <b>kısayolsuz</b> çalışır: <see cref="DiffView"/> bağımsız bir
    /// bileşen ve karşılaştırma penceresinde de kullanılıyor; orada kısayol kaydı olmayabilir.
    /// </remarks>
    public ShortcutDispatcher AttachShortcuts(ICommandRegistry registry)
    {
        ShortcutDispatcher dispatcher = new(registry, CommandContext.Diff);

        // 🔴 Arama kutusundayken gezinme ve çıplak harf kısayolları ÇALIŞMAMALI: `S`
        // kısayolu metin kutusuna yazarken satır stage'lerdi. Bu koşul kısayol dağıtımının
        // ÖNÜNDE duruyor, tek tek komutların içinde değil — biri unutulursa sessizce
        // yazmayı bozardı.
        dispatcher.Bind(CommandIds.DiffNextChange, () => WhenNotSearching(m => Move(m.GoToNextChange())));
        dispatcher.Bind(CommandIds.DiffPreviousChange, () => WhenNotSearching(m => Move(m.GoToPreviousChange())));
        dispatcher.Bind(CommandIds.DiffNextHunk, () => WhenNotSearching(m => Move(m.GoToNextHunk())));
        dispatcher.Bind(CommandIds.DiffPreviousHunk, () => WhenNotSearching(m => Move(m.GoToPreviousHunk())));
        dispatcher.Bind(CommandIds.DiffNextFile, () => WhenNotSearching(m => Move(m.GoToNextFile())));
        dispatcher.Bind(CommandIds.DiffPreviousFile, () => WhenNotSearching(m => Move(m.GoToPreviousFile())));

        dispatcher.Bind(CommandIds.DiffStageLines,
            () => WhenNotSearching(m => Started(m.StageSelectionAsync(SelectedLineIndices()))));
        dispatcher.Bind(CommandIds.DiffUnstageLines,
            () => WhenNotSearching(m => Started(m.UnstageSelectionAsync(SelectedLineIndices()))));
        dispatcher.Bind(CommandIds.DiffResetLines,
            () => WhenNotSearching(m => Started(m.DiscardSelectionAsync(SelectedLineIndices()))));

        dispatcher.Bind(CommandIds.DiffCopyCode,
            () => WhenNotSearching(m => Started(CopyAsync(m, DiffCopyMode.Code))));
        dispatcher.Bind(CommandIds.DiffCopyPatch,
            () => WhenNotSearching(m => Started(CopyAsync(m, DiffCopyMode.Patch))));

        dispatcher.Bind(CommandIds.DiffFind, () =>
        {
            LineSearch.Focus();
            LineSearch.SelectAll();
        });

        // Bul/önceki-sonraki arama kutusunda DA çalışır — aramaya devam etmenin yolu bu.
        dispatcher.Bind(CommandIds.DiffFindNext, () => Find(next: true));
        dispatcher.Bind(CommandIds.DiffFindPrevious, () => Find(next: false));

        _dispatcher = dispatcher;

        return dispatcher;
    }

    /// <summary>
    /// Panele odaklanır (P08-T05).
    /// </summary>
    /// <remarks>
    /// Odaklanacak satır yoksa <see langword="false"/> dönüyor ki panel gezinmesi burada
    /// takılmasın: boş bir diff paneline odak vermek, tuşların hiçbir yere gitmemesi demekti.
    /// </remarks>
    public bool FocusPanel()
    {
        ListBox list = ActiveList;

        return list.ItemCount > 0
            && list.ContainerFromIndex(Math.Max(0, list.SelectedIndex))?.Focus() == true;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        bool inSearchBox = LineSearch.IsFocused;

        // Kayda girmeyen iki tuş: ikisi de arama kutusuna ait ve yeniden atanabilir olmaları
        // anlamsız (Enter "onayla", Escape "vazgeç" — bunlar kısayol değil, kutu davranışı).
        switch (e.Key)
        {
            case Key.Enter when inSearchBox:
                // Odak kutuda KALIR: kullanıcı aramaya devam edebilmeli.
                Move(model.FindNext(), e, keepFocus: true);
                return;

            case Key.Escape when inSearchBox:
                FocusLines();
                e.Handled = true;
                return;

            default:
                break;
        }

        _dispatcher?.Handle(e);
    }

    /// <summary>Arama kutusunda değilken çalışır; kutudayken olayı tüketmez.</summary>
    private bool WhenNotSearching(Func<DiffViewModel, bool> action) =>
        !LineSearch.IsFocused && Model is { } model && action(model);

    /// <summary>Gezinmeyi çalıştırır, satırı görünür yapar ve odağı listeye geri verir.</summary>
    private bool Move(bool moved)
    {
        if (moved)
        {
            ScrollCurrentIntoView();
            FocusLines();
        }

        return moved;
    }

    private bool Find(bool next)
    {
        if (Model is not { } model)
        {
            return false;
        }

        bool inSearchBox = LineSearch.IsFocused;
        bool moved = next ? model.FindNext() : model.FindPrevious();

        if (moved)
        {
            ScrollCurrentIntoView();

            if (!inSearchBox)
            {
                FocusLines();
            }
        }

        return moved;
    }

    /// <summary>Başlatılmış bir işi "tüketildi" olarak bildirir.</summary>
    /// <remarks>
    /// İşlem asenkron; sonucunu beklemek tuş olayını bloke ederdi. Tuşun tüketilmesi
    /// <b>işin başlatılmış olmasına</b> bağlı, bitmesine değil.
    /// </remarks>
    private static bool Started(Task operation) => operation is not null;

    /// <summary>
    /// Gezinme başarılıysa olayı tüketir, satırı görünür yapar ve odağı listede tutar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Başarısızsa <b>tüketilmiyor</b>: dosyanın sonundayken <c>Ctrl+↓</c>'yi yutmak
    /// kullanıcıya sessiz bir duvar olurdu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Odağın geri verilmesi şart — bir test bunu yakaladı.</b> Dosya değişince liste
    /// yeniden kuruluyor ve odaklı <c>ListBoxItem</c> yok oluyor; odak taşınmazsa <b>sonraki
    /// tuş olayı görünümün ağacından hiç geçmiyor</b> ve klavye sessizce ölüyor. Faz 03'te
    /// commit listesinde ölçülen tuzağın aynısı.
    /// </para>
    /// </remarks>
    private void Move(bool moved, KeyEventArgs e, bool keepFocus = false)
    {
        if (!moved)
        {
            return;
        }

        e.Handled = true;
        ScrollCurrentIntoView();

        if (!keepFocus)
        {
            FocusLines();
        }
    }

    private ListBox ActiveList =>
        Model?.ShowSideBySide == true ? SideDiffLines : DiffLines;

    private void ScrollCurrentIntoView()
    {
        if (Model is not { CurrentLineIndex: >= 0 } model)
        {
            return;
        }

        ActiveList.SelectedIndex = model.CurrentLineIndex;
        ActiveList.ScrollIntoView(model.CurrentLineIndex);
    }

    /// <summary>
    /// Odağı diff listesine verir.
    /// </summary>
    /// <remarks>
    /// ⚠️ Faz 03'te ölçüldü: <c>ListBox.Focusable</c> <see langword="false"/>'tur, odaklanan
    /// şey <c>ListBoxItem</c>'dır. Listeyi odaklamaya çalışmak sessizce hiçbir şey yapmaz.
    /// </remarks>
    private void FocusLines()
    {
        ListBox list = ActiveList;
        int index = Math.Max(0, list.SelectedIndex);

        if (list.ContainerFromIndex(index)?.Focus() == true)
        {
            return;
        }

        // ⚠️ Dosya değişince liste yeniden kuruluyor ve konteynerler HENÜZ YOK; odak
        // sessizce hiçbir yere gitmiyor ve sonraki tuş görünüme ulaşmıyor. Bir test bunu
        // yakaladı (Alt+↓ ile dosya değiştirdikten sonra Alt+↑ ölüyordu).
        // Faz 03'te commit listesinde de odak devri bu yüzden ertelenmişti.
        Dispatcher.UIThread.Post(
            () => list.ContainerFromIndex(Math.Max(0, list.SelectedIndex))?.Focus(),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Seçili satırları panoya kopyalar; seçim yoksa tüm dosyayı.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ÖLÇÜLDÜ:</b> Avalonia 12'de <c>IClipboard.GetTextAsync</c> <b>yok</b>; okuma
    /// <c>TryGetTextAsync()</c> uzantısıyla, <c>TryGetDataAsync()</c> ise parametresiz olup
    /// <c>IAsyncDataTransfer</c> döndürüyor (sürükle-bırak API'siyle aynı değişiklik).
    /// Yazma tarafı <c>SetTextAsync</c> uzantısı ve <b>headless'ta da çalışıyor</b>, bu
    /// yüzden kopyalama testle doğrulanabiliyor.
    /// </remarks>
    private async Task CopyAsync(DiffViewModel model, DiffCopyMode mode)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard is null)
        {
            return;
        }

        string text = model.CopyText(mode, SelectedLineIndices());

        if (text.Length > 0)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private IReadOnlyList<int> SelectedLineIndices()
    {
        ListBox list = ActiveList;

        if (list.SelectedItems is not { Count: > 0 } selected)
        {
            return [];
        }

        List<int> indices = [];

        foreach (object? item in selected)
        {
            int index = list.Items.IndexOf(item);

            if (index >= 0)
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    // ---- P05-T10: kısmi staging ve bağlam menüsü ----

    private void OnStageLinesClick(object? sender, RoutedEventArgs e) =>
        Apply(model => model.StageSelectionAsync(SelectedLineIndices()));

    private void OnUnstageLinesClick(object? sender, RoutedEventArgs e) =>
        Apply(model => model.UnstageSelectionAsync(SelectedLineIndices()));

    // P05-T15: yıkıcı — onay ve yedek WorkingTreeViewModel tarafında.
    private void OnResetLinesClick(object? sender, RoutedEventArgs e) =>
        Apply(model => model.DiscardSelectionAsync(SelectedLineIndices()));

    private void OnCopyCodeClick(object? sender, RoutedEventArgs e) => Copy(DiffCopyMode.Code);

    private void OnCopyPatchClick(object? sender, RoutedEventArgs e) => Copy(DiffCopyMode.Patch);

    private void OnCopyNewClick(object? sender, RoutedEventArgs e) => Copy(DiffCopyMode.NewVersion);

    private void OnCopyOldClick(object? sender, RoutedEventArgs e) => Copy(DiffCopyMode.OldVersion);

    private void Copy(DiffCopyMode mode)
    {
        if (Model is { } model)
        {
            _ = CopyAsync(model, mode);
        }
    }

    private void Apply(Func<DiffViewModel, Task> operation)
    {
        if (Model is { } model)
        {
            _ = operation(model);
        }
    }
}
