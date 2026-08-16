using System.Globalization;
using Avalonia.Data.Converters;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Full opacity when <see langword="true"/>, dimmed otherwise (P08-T04).
/// </summary>
/// <remarks>
/// Commands that cannot run are <b>dimmed, not hidden</b>, in the command palette: hiding them would
/// leave a user who searched and did not find one saying "there is no such thing". Dimmed, it shows
/// both that it exists and why it does not work right now (the repository is closed).
/// </remarks>
public sealed class OpacityWhenTrue : IValueConverter
{
    public static OpacityWhenTrue Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.45;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
