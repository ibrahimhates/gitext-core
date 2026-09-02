using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The delete tag dialog (P14-T03) — GitExtensions' <c>FormDeleteTag</c>.
/// </summary>
public partial class DeleteTagDialog : Window
{
    private DeleteTagDecision _decision = DeleteTagDecision.Cancelled;

    public DeleteTagDialog()
    {
        InitializeComponent();

        Loaded += (_, _) => DeleteButton.Focus();
    }

    internal static async Task<DeleteTagDecision> ShowAsync(DeleteTagRequest request, Window owner)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(owner);

        DeleteTagDialog dialog = new();
        dialog.Apply(request);

        await dialog.ShowDialog(owner);

        return dialog._decision;
    }

    internal void Apply(DeleteTagRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TagNameText.Text = request.Name;
        DeleteButton.IsEnabled = request.Name.Length > 0;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (TagNameText.Text is not { Length: > 0 } name)
        {
            return;
        }

        _decision = new DeleteTagDecision { Confirmed = true, Name = name };

        Close();
    }
}
