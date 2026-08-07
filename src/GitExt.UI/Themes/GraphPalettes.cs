using Avalonia.Media;
using Avalonia.Styling;
using GitExt.UI.Settings;

namespace GitExt.UI.Themes;

/// <summary>
/// Commit grafiğinin şerit renkleri (P08-T09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Dört palet var, çünkü iki eksen bağımsız:</b> zemin (açık/koyu) ve renk ayrımı
/// (varsayılan / renk körlüğü uyumlu). Açık zeminde okunaklı bir renk koyu zeminde sönük
/// kalır; renk körlüğü uyumlu bir palet de her iki zeminde ayrı ayrı ayarlanmalı.
/// </para>
/// <para>
/// <b>Renk körlüğü uyumlu palet Okabe–Ito kümesidir</b> — deuteranopi, protanopi ve
/// tritanopi altında ayırt edilebilirliği için tasarlanmış, yaygın olarak kullanılan sekiz
/// renk. Kendi karışımımızı uydurmak yerine bunun seçilmesinin sebebi basit: "bana ayırt
/// edilebilir göründü" bir doğrulama değildir.
/// </para>
/// <para>
/// <b>Kaynak:</b> Okabe, M. &amp; Ito, K., <i>Color Universal Design</i> (2008).
/// </para>
/// </remarks>
public static class GraphPalettes
{
    /// <summary>
    /// Açık zemin, varsayılan palet.
    /// </summary>
    /// <remarks>
    /// Faz 03'ten devralındı. Kırmızı ve yeşil <b>yan yana</b> kullanıyor; deuteranopili bir
    /// kullanıcı için iki şerit ayırt edilemez. Bu yüzden erişilebilir alternatif şart
    /// (aşağıda), ama varsayılan değiştirilmedi: alışılmış görünümü sebepsiz bozmamak için.
    /// </remarks>
    public static IReadOnlyList<Color> LightDefault { get; } =
    [
        Color.FromRgb(0x45, 0x7B, 0x9D),
        Color.FromRgb(0xE6, 0x3A, 0x35),
        Color.FromRgb(0x2A, 0x9D, 0x8F),
        Color.FromRgb(0xE9, 0xC4, 0x6A),
        Color.FromRgb(0x8E, 0x7D, 0xBE),
        Color.FromRgb(0xF4, 0xA2, 0x61),
        Color.FromRgb(0x26, 0x46, 0x53),
        Color.FromRgb(0xB5, 0x65, 0x76),
    ];

    /// <summary>
    /// Koyu zemin, varsayılan palet.
    /// </summary>
    /// <remarks>
    /// Açık paletin aynısı değil: <c>#264653</c> gibi koyu tonlar koyu zeminde <b>hiç
    /// görünmez</b>. Tonlar açıldı, doygunluk korundu.
    /// </remarks>
    public static IReadOnlyList<Color> DarkDefault { get; } =
    [
        Color.FromRgb(0x74, 0xB0, 0xE0),
        Color.FromRgb(0xFF, 0x7B, 0x72),
        Color.FromRgb(0x4E, 0xD8, 0xC2),
        Color.FromRgb(0xF2, 0xD4, 0x8F),
        Color.FromRgb(0xB4, 0xA5, 0xE8),
        Color.FromRgb(0xFF, 0xB1, 0x6E),
        Color.FromRgb(0x8A, 0xB4, 0xC4),
        Color.FromRgb(0xE0, 0x92, 0xA3),
    ];

    /// <summary>Açık zemin, Okabe–Ito.</summary>
    /// <remarks>
    /// <para>
    /// Yedi renk kanonik Okabe–Ito. Tek sapma: kümenin sarısı (<c>#F0E442</c>) beyaz zeminde
    /// <b>1,1:1</b> kontrastla neredeyse görünmüyor; yerine koyu hardal (<c>#8A6D00</c>,
    /// 4,9:1) kullanıldı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Dürüst sınır:</b> gök mavisi (2,3:1) ve turuncu (2,3:1) beyaz üzerinde WCAG'in
    /// metin dışı öğeler için istediği <b>3:1'in altında</b>. Denendi ve <b>çözülemedi</b>:
    /// sekiz rengi 3:1'e çıkarmak için koyulaştırmak, onları renk körlüğü altında birbirine
    /// yaklaştırıyor — bir testle ölçüldü, iki kısıt aynı anda sağlanamıyor.
    /// </para>
    /// <para>
    /// Kanonik küme korundu çünkü <b>şeridin kimliği rengiyle değil, sütun konumuyla</b>
    /// taşınıyor; renk ikincil bilgi. Ayrıca Faz 03'te ölçüldü: gerçek depolarda eşzamanlı
    /// şerit sayısı 2–3, yani beşinci ve sonraki renkler nadiren ekrana geliyor.
    /// Sınır P08-T20'de belgeleniyor.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Color> LightColorBlindSafe { get; } =
    [
        Color.FromRgb(0x00, 0x72, 0xB2),
        Color.FromRgb(0xD5, 0x5E, 0x00),
        Color.FromRgb(0x00, 0x9E, 0x73),
        Color.FromRgb(0xCC, 0x79, 0xA7),
        Color.FromRgb(0x56, 0xB4, 0xE9),
        Color.FromRgb(0xE6, 0x9F, 0x00),
        Color.FromRgb(0x00, 0x00, 0x00),
        Color.FromRgb(0x8A, 0x6D, 0x00),
    ];

    /// <summary>Koyu zemin, Okabe–Ito.</summary>
    /// <remarks>
    /// Koyu mavi (<c>#0072B2</c>) ve siyah koyu zeminde kayboluyor; karşılıkları açık mavi
    /// ve açık gri. Sarı burada <b>kullanılabiliyor</b> — açık zeminde kullanılamayan tam da
    /// oydu. Bu palette <b>sekiz rengin de</b> zemine karşı kontrastı 4:1'in üstünde;
    /// açık temadaki uzlaşma burada gerekmedi.
    /// </remarks>
    public static IReadOnlyList<Color> DarkColorBlindSafe { get; } =
    [
        Color.FromRgb(0x56, 0xB4, 0xE9),
        Color.FromRgb(0xE6, 0x9F, 0x00),
        Color.FromRgb(0x00, 0x9E, 0x73),
        Color.FromRgb(0xCC, 0x79, 0xA7),
        Color.FromRgb(0x8F, 0xD2, 0xFF),
        Color.FromRgb(0xF0, 0xE4, 0x42),
        Color.FromRgb(0xBF, 0xBF, 0xBF),
        Color.FromRgb(0xD5, 0x5E, 0x00),
    ];

    /// <summary>Zemin ve tercihe göre paleti seçer.</summary>
    public static IReadOnlyList<Color> Resolve(ThemeVariant variant, PalettePreference preference)
    {
        bool dark = variant == ThemeVariant.Dark;

        return preference switch
        {
            PalettePreference.ColorBlindSafe => dark ? DarkColorBlindSafe : LightColorBlindSafe,
            _ => dark ? DarkDefault : LightDefault,
        };
    }

    /// <summary>Grafik paletinin kaynak sözlüğündeki anahtarı.</summary>
    public const string ResourceKey = "GitExtGraphPalette";
}
