using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A bold font when <see langword="true"/> (P06-T13).
/// </summary>
/// <remarks>
/// For telling the branch you are on apart. It is written as a converter because doing arithmetic in a
/// binding has silently misbehaved in this project before (Phase 03).
/// </remarks>
public sealed class BoldWhenTrue : IValueConverter
{
    public static BoldWhenTrue Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
