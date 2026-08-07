using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.UI.Commands;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Komut paleti (P08-T04).
/// </summary>
/// <remarks>
/// Keşfedilebilirliği tek başına çözüyor: bir komutun adını bilen, kısayolunu bilmeden de
/// ona ulaşabiliyor — ve arama sonucunda kısayolu <b>öğreniyor</b>. Menüde gizli kalan
/// komutlar için tek gerçekçi keşif yolu bu.
/// </remarks>
public sealed partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly ICommandRegistry _registry;
    private readonly CommandRouter _router;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public CommandPaletteViewModel(ICommandRegistry registry, CommandRouter router)
    {
        _registry = registry;
        _router = router;

        Refresh();
    }

    public ObservableCollection<CommandPaletteItem> Results { get; } = [];

    public bool IsEmpty => Results.Count == 0;

    /// <summary>Seçili komutu çalıştırır.</summary>
    /// <returns>Çalıştırıldıysa <see langword="true"/> — palet ancak o zaman kapanmalı.</returns>
    public bool RunSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Results.Count)
        {
            return false;
        }

        CommandPaletteItem item = Results[SelectedIndex];

        // Çalıştırılamayan komut palette görünüyor ama SOLUK; seçilse bile çalışmıyor.
        // Gizlemek daha kötü olurdu: kullanıcı komutu arar, bulamaz ve "yok" sanır.
        return item.CanRun && _router.Run(item.CommandId);
    }

    /// <summary>Seçimi listede kaydırır; uçlarda başa/sona sarar.</summary>
    /// <remarks>
    /// Sarma bilinçli: palet kısa bir liste ve kullanıcı aşağı basmayı sürdürüyor. Uçta
    /// takılmak, listenin bittiğini fark etmeyen kullanıcıya sessiz bir duvar olurdu.
    /// </remarks>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            SelectedIndex = -1;

            return;
        }

        int next = (SelectedIndex + delta) % Results.Count;

        SelectedIndex = next < 0 ? next + Results.Count : next;
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        Results.Clear();

        foreach (CommandDefinition definition in _registry.Definitions.OrderBy(Rank).ThenBy(d => d.Title))
        {
            if (!Matches(definition))
            {
                continue;
            }

            Results.Add(new CommandPaletteItem(
                definition.Id,
                definition.Title,
                definition.Category.ToString(),
                _registry.GetGesture(definition.Id)?.ToString() ?? "",
                _router.CanRun(definition.Id)));
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;

        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// Sıralama önceliği: çalıştırılabilir komutlar önce.
    /// </summary>
    /// <remarks>
    /// Depo açık değilken listenin başında "Push…" durması, paletin ilk denemede işe
    /// yaramaması demekti — Enter'a basan kullanıcı hiçbir şey olmadığını görür.
    /// </remarks>
    private int Rank(CommandDefinition definition) => _router.CanRun(definition.Id) ? 0 : 1;

    /// <summary>
    /// Sorgu eşleşmesi: harflerin <b>sırayla</b> geçmesi yeterli.
    /// </summary>
    /// <remarks>
    /// "dlo" → "Dal oluştur". Tam alt dize araması, kısaltma yazan kullanıcıyı boş sonuçla
    /// karşılardı; bulanık eşleşme paletin işe yaramasının şartı.
    /// </remarks>
    private bool Matches(CommandDefinition definition)
    {
        if (Query.Length == 0)
        {
            return true;
        }

        return IsSubsequence(Query, definition.Title)
            || IsSubsequence(Query, definition.Id)
            || _registry.GetGesture(definition.Id)?.ToString()
                .Contains(Query, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// <paramref name="needle"/> harfleri <paramref name="haystack"/> içinde sırayla geçiyor mu?
    /// </summary>
    /// <remarks>
    /// Karşılaştırma <b>kültüre duyarlı</b>: Türkçe'de <c>I</c>/<c>ı</c> ve <c>İ</c>/<c>i</c>
    /// ayrı harfler. Ordinal karşılaştırma "İşlem" yazan kullanıcıya sonuç döndürmezdi.
    /// </remarks>
    private static bool IsSubsequence(string needle, string haystack)
    {
        int index = 0;

        foreach (char c in haystack)
        {
            if (index < needle.Length
                && char.ToLower(c, System.Globalization.CultureInfo.CurrentCulture)
                    == char.ToLower(needle[index], System.Globalization.CultureInfo.CurrentCulture))
            {
                index++;
            }
        }

        return index == needle.Length;
    }
}

/// <summary>Palette görünen bir satır.</summary>
public sealed record CommandPaletteItem(
    string CommandId,
    string Title,
    string Category,
    string GestureText,
    bool CanRun);
