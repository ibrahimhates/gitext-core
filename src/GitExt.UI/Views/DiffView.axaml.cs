using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

    private DiffViewModel? Model => DataContext as DiffViewModel;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // Arama kutusundayken gezinme tuşları metne müdahale etmemeli; yalnızca
        // Enter/Escape ve F3 anlamlı.
        bool inSearchBox = LineSearch.IsFocused;

        switch (e.Key)
        {
            case Key.Down when control && !inSearchBox:
                Move(model.GoToNextChange(), e);
                break;

            case Key.Up when control && !inSearchBox:
                Move(model.GoToPreviousChange(), e);
                break;

            case Key.PageDown when control && !inSearchBox:
                Move(model.GoToNextHunk(), e);
                break;

            case Key.PageUp when control && !inSearchBox:
                Move(model.GoToPreviousHunk(), e);
                break;

            case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !inSearchBox:
                Move(model.GoToNextFile(), e);
                break;

            case Key.Up when e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !inSearchBox:
                Move(model.GoToPreviousFile(), e);
                break;

            case Key.F when control:
                LineSearch.Focus();
                LineSearch.SelectAll();
                e.Handled = true;
                break;

            case Key.Enter when inSearchBox:
                // Odak kutuda KALIR: kullanıcı aramaya devam edebilmeli.
                Move(shift ? model.FindPrevious() : model.FindNext(), e, keepFocus: true);
                break;

            case Key.F3:
                Move(shift ? model.FindPrevious() : model.FindNext(), e, keepFocus: inSearchBox);
                break;

            case Key.Escape when inSearchBox:
                FocusLines();
                e.Handled = true;
                break;

            case Key.C when control && !inSearchBox:
                // Ctrl+Shift+C yamayı önekleriyle kopyalar; düz Ctrl+C yalnızca kodu.
                _ = CopyAsync(model, shift ? DiffCopyMode.Patch : DiffCopyMode.Code);
                e.Handled = true;
                break;
        }
    }

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
}
