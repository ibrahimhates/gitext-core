using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Turns a <see cref="WorkingTreeRow.Depth"/> into the row's left indent.
/// </summary>
/// <remarks>
/// The file tree is drawn as a flat, virtualised row list rather than a nested control, so the
/// indent is the only thing that shows the hierarchy. It is a converter, not arithmetic in a binding:
/// expressions inside bindings have silently misbehaved in this project before (Phase 03).
/// </remarks>
public sealed class DepthIndent : IValueConverter
{
    /// <summary>Indent per level, in device-independent pixels.</summary>
    public const double Step = 14;

    public static DepthIndent Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is int depth && depth > 0 ? depth * Step : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
