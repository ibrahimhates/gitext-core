using Avalonia.Controls;
using GitExt.UI.Localization;

namespace GitExt.UI.Views;

/// <summary>
/// Asks for a dashboard category name (P12-T03).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is <c>FormDashboardCategoryTitle</c>; the title changes to
/// "Rename category" when an existing name is being edited, as it does there.
/// </remarks>
public partial class DashboardCategoryDialog : Window
{
    private IReadOnlyList<string> _existing = [];
    private string? _result;

    public DashboardCategoryDialog()
    {
        InitializeComponent();

        // ⚠️ A property change rather than `TextChanged`: `TextChanged` does not fire in a window
        // that is not attached to the visual tree (measured in P06-T01), and the validation was
        // silently never running.
        CategoryNameBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Revalidate();
            }
        };

        Loaded += (_, _) => CategoryNameBox.Focus();

        Revalidate();
    }

    /// <summary>Opens the dialog modally and returns the name, or <see langword="null"/>.</summary>
    internal static async Task<string?> ShowAsync(
        IReadOnlyList<string> existingCategories,
        string? currentName,
        Window owner)
    {
        ArgumentNullException.ThrowIfNull(existingCategories);
        ArgumentNullException.ThrowIfNull(owner);

        DashboardCategoryDialog dialog = new();
        dialog.Apply(existingCategories, currentName);

        await dialog.ShowDialog(owner);

        return dialog._result;
    }

    /// <summary>Reflects the request onto the dialog.</summary>
    internal void Apply(IReadOnlyList<string> existingCategories, string? currentName)
    {
        ArgumentNullException.ThrowIfNull(existingCategories);

        _existing = existingCategories;

        if (currentName is not null)
        {
            Title = Loc.T("dashboard.rename_category");
            CategoryNameBox.Text = currentName;
            CategoryNameBox.SelectAll();
        }

        Revalidate();
    }

    /// <summary>The name entered, or <see langword="null"/> when the dialog was cancelled.</summary>
    internal string? Category => _result;

    /// <summary>
    /// Writes down why the name cannot be used and disables OK.
    /// </summary>
    /// <remarks>
    /// An empty box is not an error message: telling a user off before they have typed anything
    /// is noise. The button is disabled all the same.
    /// </remarks>
    private void Revalidate()
    {
        string name = (CategoryNameBox.Text ?? string.Empty).Trim();
        bool empty = name.Length == 0;
        bool duplicate = !empty && _existing.Contains(name, StringComparer.CurrentCulture);

        OkButton.IsEnabled = !empty && !duplicate;

        ValidationText.IsVisible = duplicate;
        ValidationText.Text = duplicate ? Loc.T("dashboard.category_name_exists") : string.Empty;
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnOkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string name = (CategoryNameBox.Text ?? string.Empty).Trim();

        if (name.Length == 0 || _existing.Contains(name, StringComparer.CurrentCulture))
        {
            return;
        }

        _result = name;
        Close();
    }
}
