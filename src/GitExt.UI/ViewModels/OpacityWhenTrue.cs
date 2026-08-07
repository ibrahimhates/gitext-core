using System.Globalization;
using Avalonia.Data.Converters;

namespace GitExt.UI.ViewModels;

/// <summary>
/// <see langword="true"/> ise tam opaklık, değilse soluk (P08-T04).
/// </summary>
/// <remarks>
/// Çalıştırılamayan komutlar komut paletinde <b>gizlenmiyor, soluklaştırılıyor</b>: gizlemek,
/// komutu arayıp bulamayan kullanıcıya "böyle bir şey yok" dedirtirdi. Soluk hâli hem
/// varlığını hem şu an neden çalışmadığını (depo kapalı) gösteriyor.
/// </remarks>
public sealed class OpacityWhenTrue : IValueConverter
{
    public static OpacityWhenTrue Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.45;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
