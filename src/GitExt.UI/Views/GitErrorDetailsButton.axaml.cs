using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The button that opens a failed git command's full output in a separate window (P05-T07).
/// </summary>
/// <remarks>
/// It takes its own <see cref="Details"/> property rather than binding to the <c>DataContext</c>: the
/// same error is shown through two separate ViewModel routes (<c>ErrorDetails</c> and
/// <c>Commits.ErrorDetails</c>).
/// </remarks>
public partial class GitErrorDetailsButton : UserControl
{
    /// <summary>The output to show; the button is invisible when <see langword="null"/>.</summary>
    public static readonly StyledProperty<GitOutputViewModel?> DetailsProperty =
        AvaloniaProperty.Register<GitErrorDetailsButton, GitOutputViewModel?>(nameof(Details));

    public GitErrorDetailsButton()
    {
        InitializeComponent();

        // Hidden to begin with: if `Details` is never assigned (null → null) no property change fires
        // and an empty button would sit on screen.
        IsVisible = false;
    }

    /// <inheritdoc cref="DetailsProperty"/>
    public GitOutputViewModel? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DetailsProperty)
        {
            IsVisible = change.GetNewValue<GitOutputViewModel?>() is not null;
        }
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (Details is { } details)
        {
            GitOutputWindow.Open(details, TopLevel.GetTopLevel(this) as Window);
        }
    }
}
