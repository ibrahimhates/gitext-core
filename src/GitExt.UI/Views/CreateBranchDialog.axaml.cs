using Avalonia.Controls;
using GitExt.Core;
using GitExt.UI.ViewModels;

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
            ? "Bu revizyonda dal oluştur (mevcut HEAD)"
            : "Bu revizyonda dal oluştur";

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
            ? "Çalışma ağacında kaydedilmemiş değişiklikler var. Bunlar yeni dala taşınır; "
              + "taşınamayacak bir çakışma olursa git işlemi reddeder ve hiçbir şey değişmez."
            : string.Empty;
    }

    internal static string Describe(BranchNameProblem problem) => problem switch
    {
        BranchNameProblem.Empty => "Dal adı boş olamaz.",
        BranchNameProblem.NestedRefsPrefix =>
            "\"refs/heads/\" önekini yazmayın — git bunu hata saymaz, iç içe bir dal oluşturur.",
        BranchNameProblem.RevisionSyntax =>
            "\"@{…}\" git için revizyon sözdizimi; yazdığınızdan başka bir dal adı oluşurdu.",
        BranchNameProblem.LeadingDash => "Dal adı \"-\" ile başlayamaz.",
        BranchNameProblem.ReservedHead => "\"HEAD\" git tarafından ayrılmış bir addır.",
        BranchNameProblem.ForbiddenCharacter =>
            "Dal adında boşluk ve şu karakterler kullanılamaz: ~ ^ : ? * [ \\",
        BranchNameProblem.InvalidSegment =>
            "Bölümler \".\" ile başlayamaz ve \".lock\" ile bitemez.",
        BranchNameProblem.EmptySegment => "Dal adı \"/\" ile başlayıp bitemez, \"//\" içeremez.",
        BranchNameProblem.InvalidDot => "Dal adı \"..\" içeremez ve \".\" ile bitemez.",
        _ => "Geçersiz dal adı.",
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
