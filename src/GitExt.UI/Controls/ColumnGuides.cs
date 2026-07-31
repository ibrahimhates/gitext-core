using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace GitExt.UI.Controls;

/// <summary>
/// Belirli sütun numaralarında dikey kılavuz çizgileri çizer (P05-T12).
/// </summary>
/// <remarks>
/// <para>
/// Commit mesajında yerleşik gelenek: <b>konu satırı ≤ 50</b>, <b>gövde satırları ≤ 72</b>.
/// Kılavuz kullanıcıya sınırı <i>yazarken</i> gösteriyor — commit anında uyarmak, mesajı
/// zaten yazılmışken düzeltmeye zorlamak olurdu.
/// </para>
/// <para>
/// <b>ÖLÇÜLDÜ (P05-T12):</b> sütun kılavuzu sabit karakter genişliği gerektiriyor ve
/// <c>"Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"</c> zinciri headless'ta
/// <b>gerçekten monospace</b> çözülüyor (<c>iiii</c> ve <c>MMMM</c> aynı genişlikte).
/// ⚠️ Zincirdeki adlar <b>tek tek</b> verilirse çalışmıyor: bu makinede <c>Cascadia Mono</c>
/// ve <c>Consolas</c> yok ve orantılı bir yedeğe düşüyorlar (<c>iiii</c>=12,4 ·
/// <c>MMMM</c>=43,5). Zincir olarak verildiğinde sıradaki ada geçiliyor.
/// </para>
/// </remarks>
public sealed class ColumnGuides : Control
{
    /// <summary>Kılavuz çizilecek sütunlar.</summary>
    public static readonly StyledProperty<IReadOnlyList<int>> ColumnsProperty =
        AvaloniaProperty.Register<ColumnGuides, IReadOnlyList<int>>(nameof(Columns), [50, 72]);

    /// <summary>Çizgi rengi.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ColumnGuides, IBrush?>(nameof(Stroke));

    /// <summary>Metnin sol kenar boşluğu — kutunun iç dolgusuyla aynı olmalı.</summary>
    public static readonly StyledProperty<double> TextOffsetProperty =
        AvaloniaProperty.Register<ColumnGuides, double>(nameof(TextOffset));

    static ColumnGuides()
    {
        AffectsRender<ColumnGuides>(
            ColumnsProperty,
            StrokeProperty,
            TextOffsetProperty,
            TextElement.FontFamilyProperty,
            TextElement.FontSizeProperty);
    }

    /// <inheritdoc cref="ColumnsProperty"/>
    public IReadOnlyList<int> Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <inheritdoc cref="StrokeProperty"/>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <inheritdoc cref="TextOffsetProperty"/>
    public double TextOffset
    {
        get => GetValue(TextOffsetProperty);
        set => SetValue(TextOffsetProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        IBrush? stroke = Stroke;

        if (stroke is null || Columns.Count == 0 || Bounds.Height <= 0)
        {
            return;
        }

        double characterWidth = MeasureCharacterWidth();

        if (characterWidth <= 0)
        {
            return;
        }

        Pen pen = new(stroke, 1);

        foreach (int column in Columns)
        {
            // Yarım piksel kaydırma: tam sayıda çizilen 1 px'lik çizgi iki piksele yayılıp
            // soluk görünüyor.
            double x = Math.Floor(TextOffset + (column * characterWidth)) + 0.5;

            if (x > Bounds.Width)
            {
                continue;
            }

            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        }
    }

    /// <summary>
    /// Tek karakterin genişliği.
    /// </summary>
    /// <remarks>
    /// Ölçüm <b>çizim zamanında</b> yapılıyor: yazı tipi ve punto değişebiliyor (P04-T13'te
    /// punto ayarı geldi) ve önbelleklenmiş bir değer sessizce eskir.
    /// </remarks>
    private double MeasureCharacterWidth()
    {
        FormattedText text = new(
            "0",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this)),
            TextElement.GetFontSize(this),
            Brushes.Black);

        return text.Width;
    }
}
