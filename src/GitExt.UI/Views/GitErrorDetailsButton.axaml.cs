using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// Başarısız git komutunun tam çıktısını ayrı pencerede açan düğme (P05-T07).
/// </summary>
/// <remarks>
/// Kendi <see cref="Details"/> özelliğini alıyor, <c>DataContext</c>'e bağlı değil: aynı
/// hata iki ayrı ViewModel yolundan gösteriliyor (<c>ErrorDetails</c> ve
/// <c>Commits.ErrorDetails</c>).
/// </remarks>
public partial class GitErrorDetailsButton : UserControl
{
    /// <summary>Gösterilecek çıktı; <see langword="null"/> ise düğme görünmez.</summary>
    public static readonly StyledProperty<GitOutputViewModel?> DetailsProperty =
        AvaloniaProperty.Register<GitErrorDetailsButton, GitOutputViewModel?>(nameof(Details));

    public GitErrorDetailsButton()
    {
        InitializeComponent();

        // Başlangıçta gizli: `Details` hiç atanmazsa (null → null) özellik değişimi
        // tetiklenmez ve boş bir düğme ekranda kalırdı.
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
