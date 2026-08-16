using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace GitExt.UI.Controls;

/// <summary>
/// Draws vertical guide lines at particular column numbers (P05-T12).
/// </summary>
/// <remarks>
/// <para>
/// The established convention for a commit message: the <b>subject line ≤ 50</b>, the <b>body lines ≤
/// 72</b>. The guide shows the user the limit <i>while they type</i> — warning at commit time would
/// mean forcing a fix on a message that is already written.
/// </para>
/// <para>
/// <b>MEASURED (P05-T12):</b> a column guide requires a fixed character width, and the chain
/// <c>"Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace"</c> really does resolve to a
/// <b>monospace</b> face headless (<c>iiii</c> and <c>MMMM</c> come out the same width).
/// ⚠️ The names in the chain do not work when given <b>individually</b>: <c>Cascadia Mono</c> and
/// <c>Consolas</c> do not exist on this machine and fall back to a proportional face
/// (<c>iiii</c>=12.4 · <c>MMMM</c>=43.5). Given as a chain, it moves on to the next name.
/// </para>
/// </remarks>
public sealed class ColumnGuides : Control
{
    /// <summary>The columns to draw guides at.</summary>
    public static readonly StyledProperty<IReadOnlyList<int>> ColumnsProperty =
        AvaloniaProperty.Register<ColumnGuides, IReadOnlyList<int>>(nameof(Columns), [50, 72]);

    /// <summary>The line colour.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ColumnGuides, IBrush?>(nameof(Stroke));

    /// <summary>The text's left margin — it must match the box's inner padding.</summary>
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
            // A half-pixel offset: a 1 px line drawn on a whole number spreads across two pixels and
            // looks faint.
            double x = Math.Floor(TextOffset + (column * characterWidth)) + 0.5;

            if (x > Bounds.Width)
            {
                continue;
            }

            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        }
    }

    /// <summary>
    /// The width of a single character.
    /// </summary>
    /// <remarks>
    /// The measurement is made <b>at draw time</b>: the font and size can change (the size setting
    /// arrived in P04-T13) and a cached value goes stale silently.
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
