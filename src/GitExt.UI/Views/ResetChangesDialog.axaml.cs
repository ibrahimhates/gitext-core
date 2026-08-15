using Avalonia.Controls;
using GitExt.Core.Model;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// Yıkıcı sıfırlama işlemi için onay diyaloğu (P05-T15).
/// </summary>
/// <remarks>
/// GitExtensions'ta karşılığı <c>FormResetChanges</c>; yerleşim ve düğme sırası oradan
/// alındı (§ 9). Tek fark, etkilenecek dosyaların <b>listelenmesi</b>.
/// </remarks>
public partial class ResetChangesDialog : Window
{
    /// <summary>Listede gösterilecek en fazla yol.</summary>
    /// <remarks>
    /// Binlerce satırlık bir liste kimseyi bilgilendirmez; ilk birkaç yol "hangi klasör"
    /// sorusuna cevap verir, sayı zaten başlıkta yazıyor.
    /// </remarks>
    public const int PreviewLimit = 40;

    private ResetChangesDecision _decision = ResetChangesDecision.Cancelled;

    public ResetChangesDialog()
    {
        InitializeComponent();
    }

    /// <summary>Diyaloğu modal açar ve kullanıcının kararını döndürür.</summary>
    internal static async Task<ResetChangesDecision> ShowAsync(
        ResetChangesRequest request,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        ResetChangesDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    /// <summary>İsteği diyalog üzerine yansıtır.</summary>
    internal void Apply(ResetChangesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        int modified = request.ModifiedPaths.Count;
        int untracked = request.UntrackedPaths.Count;

        MessageText.Text = request.IncludesStaged
            ? $"All changes in {modified} files (including staged ones) will be reset."
            : $"Unstaged changes in {modified} files will be reset.";

        HintText.Text = Loc.T("reset_changes_dialog.axaml.this_deletes_uncommitted_work");

        AffectedList.ItemsSource = Preview(request);

        // GitExtensions'taki davranış: seçimde yeni dosya yoksa kutu kapalı ve devre dışı,
        // yalnızca yeni dosya varsa açık ve devre dışı (tek seçenek o).
        DeleteUntrackedBox.Content = $"Also delete {untracked} new files and/or directories";
        DeleteUntrackedBox.IsEnabled = untracked > 0 && modified > 0;
        DeleteUntrackedBox.IsChecked = untracked > 0 && modified == 0;

        DoNotAskAgainBox.IsVisible = request.CanSuppress;
    }

    private static IReadOnlyList<string> Preview(ResetChangesRequest request)
    {
        List<string> lines = [];

        foreach (RepositoryPath path in request.ModifiedPaths.Take(PreviewLimit))
        {
            lines.Add($"M  {path.Value}");
        }

        foreach (RepositoryPath path in request.UntrackedPaths.Take(PreviewLimit))
        {
            lines.Add($"?  {path.Value}");
        }

        int total = request.ModifiedPaths.Count + request.UntrackedPaths.Count;

        if (total > lines.Count)
        {
            lines.Add($"… ve {total - lines.Count} dosya daha");
        }

        return lines;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnResetClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _decision = new ResetChangesDecision
        {
            Confirmed = true,
            DeleteUntracked = DeleteUntrackedBox.IsChecked == true,
            DoNotAskAgain = DoNotAskAgainBox.IsChecked == true,
        };

        Close();
    }
}
