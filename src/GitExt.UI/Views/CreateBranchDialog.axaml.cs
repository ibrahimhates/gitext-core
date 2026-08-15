using Avalonia.Controls;
using GitExt.Core;
using GitExt.UI.ViewModels;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// Dal oluşturma diyaloğu (P06-T01).
/// </summary>
/// <remarks>
/// GitExtensions'ta karşılığı <c>FormCreateBranch</c>; yerleşim ve sıra oradan alındı (§ 9).
/// </remarks>
public partial class CreateBranchDialog : Window
{
    private CreateBranchDecision _decision = CreateBranchDecision.Cancelled;

    public CreateBranchDialog()
    {
        InitializeComponent();

        // ⚠️ `TextChanged` yerine özellik değişimi: `TextChanged` görsel ağaca bağlı
        // olmayan bir pencerede tetiklenmiyor (headless testte ölçüldü) ve doğrulama
        // sessizce hiç çalışmıyordu.
        BranchNameTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Revalidate();
            }
        };
        CheckoutAfterCreateBox.IsCheckedChanged += (_, _) => UpdateDirtyWarning();

        Loaded += (_, _) => BranchNameTextBox.Focus();

        Revalidate();
    }

    /// <summary>Diyaloğu modal açar ve kullanıcının kararını döndürür.</summary>
    internal static async Task<CreateBranchDecision> ShowAsync(
        CreateBranchRequest request,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        CreateBranchDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    /// <summary>İsteği diyalog üzerine yansıtır.</summary>
    internal void Apply(CreateBranchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        StartPointText.Text = request.StartPointLabel;
        StartPointLabel.Text = request.StartPoint is null
            ? Loc.T("create_branch_dialog.axaml.create_branch_at_this_revision_current_head")
            : Loc.T("create_branch_dialog.axaml.create_branch_at_this_revision");

        _hasLocalChanges = request.HasLocalChanges;

        UpdateDirtyWarning();
        Revalidate();
    }

    private bool _hasLocalChanges;

    /// <summary>
    /// Ad geçerli değilse <b>nedenini</b> yazar ve düğmeyi kapatır.
    /// </summary>
    /// <remarks>
    /// Doğrulama <see cref="BranchName"/> ile yapılıyor; her tuş vuruşunda <c>git</c> süreci
    /// başlatmamak için saf. Kuralların git ile aynı kaldığı <c>BranchNameTests</c>'teki
    /// ayrık testle sabitlendi.
    /// </remarks>
    private void Revalidate()
    {
        string name = BranchNameTextBox.Text ?? string.Empty;
        BranchNameProblem? problem = BranchName.Validate(name);

        CreateButton.IsEnabled = problem is null;

        // Boş kutuda hata metni göstermek, henüz bir şey yapmamış kullanıcıyı azarlamaktır.
        bool show = problem is not null and not BranchNameProblem.Empty;

        ValidationText.IsVisible = show;
        ValidationText.Text = show ? Describe(problem!.Value) : string.Empty;
    }

    /// <summary>
    /// Checkout işaretliyken ve çalışma ağacı kirliyken uyarır.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: <c>git switch -c</c> kirli ağaçta değişiklikleri çoğu zaman <b>taşıyor</b>,
    /// ama çakışma varsa <b>reddediyor</b> (ve dalı da oluşturmuyor). Bu yüzden bu bir engel
    /// değil bilgi: kullanıcıya ne olabileceğini söylüyoruz, kararı ona bırakıyoruz.
    /// </remarks>
    private void UpdateDirtyWarning()
    {
        bool warn = _hasLocalChanges && CheckoutAfterCreateBox.IsChecked == true;

        DirtyWarning.IsVisible = warn;
        DirtyWarning.Text = warn
            ? Loc.T("create_branch_dialog.axaml.there_are_uncommitted_changes_in_the_working")
              + Loc.T("create_branch_dialog.axaml.if_there_is_a_conflict_that_cannot_be_carrie")
            : string.Empty;
    }

    internal static string Describe(BranchNameProblem problem) => problem switch
    {
        BranchNameProblem.Empty => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_be_empty"),
        BranchNameProblem.NestedRefsPrefix =>
            Loc.T("create_branch_dialog.axaml.do_not_type_the_refs_heads_prefix_git_does_n"),
        BranchNameProblem.RevisionSyntax =>
            Loc.T("create_branch_dialog.axaml.is_revision_syntax_for_git_a_branch_name_oth"),
        BranchNameProblem.LeadingDash => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_start_with"),
        BranchNameProblem.ReservedHead => Loc.T("create_branch_dialog.axaml.head_is_a_name_reserved_by_git"),
        BranchNameProblem.ForbiddenCharacter =>
            Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_contain_spaces_or_these"),
        BranchNameProblem.InvalidSegment =>
            Loc.T("create_branch_dialog.axaml.components_cannot_start_with_or_end_with_loc"),
        BranchNameProblem.EmptySegment => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_start_or_end_with_or_co"),
        BranchNameProblem.InvalidDot => Loc.T("create_branch_dialog.axaml.a_branch_name_cannot_contain_or_end_with"),
        _ => Loc.T("create_branch_dialog.axaml.invalid_branch_name"),
    };

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnCreateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string name = BranchNameTextBox.Text ?? string.Empty;

        if (!BranchName.IsValid(name))
        {
            return;
        }

        _decision = new CreateBranchDecision
        {
            Confirmed = true,
            Name = name,
            Checkout = CheckoutAfterCreateBox.IsChecked == true,
        };

        Close();
    }
}
