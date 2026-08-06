using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GitExt.UI.ViewModels;

/// <summary>
/// <see langword="true"/> ise kalın yazı tipi (P06-T13).
/// </summary>
/// <remarks>
/// Üzerinde bulunulan dalı ayırt etmek için. Dönüştürücü olarak yazılıyor çünkü bağlamada
/// hesap yapmak bu projede daha önce sessizce yanlış davranmıştı (Faz 03).
/// </remarks>
public sealed class BoldWhenTrue : IValueConverter
{
    public static BoldWhenTrue Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
