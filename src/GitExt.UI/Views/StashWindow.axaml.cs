using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>Stash penceresi (P07-T13).</summary>
public partial class StashWindow : Window
{
    public StashWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    internal static async Task ShowAsync(StashViewModel model, Window owner)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(owner);

        StashWindow window = new() { DataContext = model };

        await window.ShowDialog(owner);
    }


    /// <remarks>
    /// Dal adı doğrudan bağlanmıyor: <see cref="StashViewModel.BranchName"/> düz bir
    /// özellik ve komutun etkinliği ona bakıyor; her tuşta yeniden değerlendirilmesi için
    /// metin değişimi burada iletiliyor.
    /// </remarks>
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (this.FindControl<TextBox>("BranchNameBox") is { } box
            && DataContext is StashViewModel model)
        {
            box.TextChanged += (_, _) =>
            {
                model.BranchName = box.Text ?? string.Empty;
                model.BranchCommand.NotifyCanExecuteChanged();
            };
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
