using Avalonia.Controls;
using GitExt.Core;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Dal yeniden adlandırma diyaloğu (P06-T03).
/// </summary>
/// <remarks>
/// GitExtensions'ta karşılığı <c>FormRenameBranch</c> (§ 9).
/// </remarks>
public partial class RenameBranchDialog : Window
{
    private RenameBranchDecision _decision = RenameBranchDecision.Cancelled;

    public RenameBranchDialog()
    {
        InitializeComponent();

        // ⚠️ `TextChanged` görsel ağaca bağlı olmayan pencerede tetiklenmiyor (P06-T01'de
        // ölçüldü); doğrulama sessizce hiç çalışmazdı.
        NewNameTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Revalidate();
            }
        };

        Loaded += (_, _) => NewNameTextBox.Focus();

        Revalidate();
    }

    internal static async Task<RenameBranchDecision> ShowAsync(
        RenameBranchRequest request,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        RenameBranchDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    internal void Apply(RenameBranchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        CurrentNameText.Text = request.CurrentName;

        // Kutu mevcut adla dolu geliyor: yeniden adlandırma çoğunlukla küçük bir düzeltme
        // (yazım hatası, önek ekleme); sıfırdan yazdırmak gereksiz iş.
        NewNameTextBox.Text = request.CurrentName;

        Revalidate();
    }

    private void Revalidate()
    {
        string name = NewNameTextBox.Text ?? string.Empty;
        BranchNameProblem? problem = BranchName.Validate(name);

        RenameButton.IsEnabled = problem is null;

        bool show = problem is not null and not BranchNameProblem.Empty;

        ValidationText.IsVisible = show;
        ValidationText.Text = show ? CreateBranchDialog.Describe(problem!.Value) : string.Empty;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnRenameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string name = NewNameTextBox.Text ?? string.Empty;

        if (!BranchName.IsValid(name))
        {
            return;
        }

        _decision = new RenameBranchDecision { Confirmed = true, NewName = name };

        Close();
    }
}
