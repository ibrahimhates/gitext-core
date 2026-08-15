using Avalonia.Controls;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// Uzak depo kaldırma onayı (P06-T05).
/// </summary>
/// <remarks>
/// Ayrı bir diyalog değil <b>onay kutusu</b> yeterli, çünkü kurtarma komutları ekranda
/// (P05-T15 kuralı). Kutu işaretlenmeden <i>Kaldır</i> etkin olmuyor.
/// </remarks>
public partial class RemoveRemoteDialog : Window
{
    private bool _confirmed;

    public RemoveRemoteDialog()
    {
        InitializeComponent();

        // `IsCheckedChanged` görsel ağaca bağlı olmayan pencerede de çalışıyor (P06-T01'de
        // ölçüldü; `TextChanged` çalışmıyor — fark yanıltıcı).
        ConfirmBox.IsCheckedChanged += (_, _) => RemoveButton.IsEnabled = ConfirmBox.IsChecked == true;

        RemoveButton.IsEnabled = false;
    }

    internal static async Task<bool> ShowAsync(RemoteRemovalRequest request, Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        RemoveRemoteDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._confirmed;
    }

    internal void Apply(RemoteRemovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        MessageText.Text = $"Remote '{request.Name}' will be removed.";
        ImpactText.Text = DescribeImpact(request);
        RecoveryCommands.Text = string.Join('\n', request.RecoveryCommands);
    }

    /// <summary>
    /// Neyin kaybedileceğini <b>somut</b> anlatır.
    /// </summary>
    /// <remarks>
    /// Tek bir genel metin ("emin misiniz?") kullanıcıya kararını verecek bilgiyi vermez:
    /// upstream'i giden dal sayısı ve silinecek uzak izleme dalı sayısı burada farkı
    /// yaratıyor (P06-T02'deki "her seçenek ne yapacağını yazar" kuralı).
    /// </remarks>
    internal static string DescribeImpact(RemoteRemovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> parts = [];

        if (request.TrackingBranchCount > 0)
        {
            parts.Add(
                $"{request.TrackingBranchCount} remote-tracking branches (and their reflogs) will be deleted; "
                + Loc.T("remove_remote_dialog.axaml.commits_that_live_only_on_them_will_no_longe"));
        }

        if (request.AffectedBranches.Count > 0)
        {
            parts.Add(
                $"these local branches will lose their upstream link: "
                + string.Join(", ", request.AffectedBranches));
        }

        if (request.IsPushDefault)
        {
            parts.Add(Loc.T("remove_remote_dialog.axaml.this_remote_is_set_as_the_default_push_targe"));
        }

        return parts.Count == 0
            ? Loc.T("remove_remote_dialog.axaml.no_local_branch_or_remote_tracking_branch_is")
            : char.ToUpperInvariant(parts[0][0]) + parts[0][1..]
              + (parts.Count > 1 ? " · " + string.Join(" · ", parts.Skip(1)) : string.Empty)
              + ".";
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnRemoveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ConfirmBox.IsChecked != true)
        {
            return;
        }

        _confirmed = true;
        Close();
    }
}
