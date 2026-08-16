using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GitExt.Graph;
using GitExt.UI.Themes;

namespace GitExt.UI.Controls;

/// <summary>
/// Draws the graph column of a single commit row (P03-T10).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why custom drawing?</b> The lane count changes from row to row; creating a variable number of
/// <c>Line</c>/<c>Ellipse</c> elements per row in XAML would be both expensive and unwieldy. The
/// graph column is the one piece that necessarily falls on the **custom drawing** side of the
/// "ready-made controls or custom drawing" question (P03-T09).
/// </para>
/// <para>
/// The scope is deliberately narrow: it draws <b>only</b> the lanes, the node and the edges. The
/// rest of the row (SHA, subject, author, date, badges) is drawn with ordinary controls in the
/// virtualised <c>ListBox</c> template — selection, keyboard navigation and accessibility come free
/// from there.
/// </para>
/// <para>
/// <b>Allocation discipline:</b> <see cref="IPen"/> and <see cref="IBrush"/> objects are cached
/// statically. Creating them every frame would mean thousands of objects per second at 60 FPS.
/// </para>
/// </remarks>
public sealed class CommitGraphCell : Control
{
    /// <summary>The horizontal distance between two lane centres.</summary>
    public static readonly StyledProperty<double> LaneWidthProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(LaneWidth), 14);

    /// <summary>The radius of the commit node.</summary>
    public static readonly StyledProperty<double> NodeRadiusProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(NodeRadius), 4);

    /// <summary>The line thickness.</summary>
    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(LineThickness), 2);

    /// <summary>This row's layout result.</summary>
    public static readonly StyledProperty<GraphRow?> RowProperty =
        AvaloniaProperty.Register<CommitGraphCell, GraphRow?>(nameof(Row));

    /// <summary>
    /// The lane colours.
    /// </summary>
    /// <remarks>
    /// The default palette is temporary; the real palette will be bound to the theme in Phase 08
    /// (colour-blind compatibility included). <see cref="GraphRow.ColorIndex"/> is mapped onto this
    /// array modulo its length.
    /// </remarks>
    public static readonly StyledProperty<IReadOnlyList<Color>?> PaletteProperty =
        AvaloniaProperty.Register<CommitGraphCell, IReadOnlyList<Color>?>(nameof(Palette));

    /// <summary>
    /// The index of the first visible lane — the horizontal position of the graph window (P03-T21).
    /// </summary>
    /// <remarks>
    /// Every row uses the same value, otherwise the lanes shift from row to row.
    /// </remarks>
    public static readonly StyledProperty<int> FirstLaneProperty =
        AvaloniaProperty.Register<CommitGraphCell, int>(nameof(FirstLane));

    /// <summary>
    /// How many lanes are shown at once (P03-T21).
    /// </summary>
    /// <remarks>
    /// <b>MEASURED:</b> in real repositories the lane count is around 120 at the median (git/git,
    /// Linux). Widening the column to the lane count pushed every other column off screen. A fixed
    /// window keeps both the alignment and the readability; the window follows the selected commit.
    /// </remarks>
    public static readonly StyledProperty<int> VisibleLanesProperty =
        AvaloniaProperty.Register<CommitGraphCell, int>(nameof(VisibleLanes), 12);

    static CommitGraphCell()
    {
        AffectsRender<CommitGraphCell>(
            RowProperty, LaneWidthProperty, NodeRadiusProperty, LineThicknessProperty,
            PaletteProperty, FirstLaneProperty, VisibleLanesProperty);

        AffectsMeasure<CommitGraphCell>(LaneWidthProperty, VisibleLanesProperty);
    }

    public double LaneWidth
    {
        get => GetValue(LaneWidthProperty);
        set => SetValue(LaneWidthProperty, value);
    }

    public int FirstLane
    {
        get => GetValue(FirstLaneProperty);
        set => SetValue(FirstLaneProperty, value);
    }

    public int VisibleLanes
    {
        get => GetValue(VisibleLanesProperty);
        set => SetValue(VisibleLanesProperty, value);
    }

    public double NodeRadius
    {
        get => GetValue(NodeRadiusProperty);
        set => SetValue(NodeRadiusProperty, value);
    }

    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    public GraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public IReadOnlyList<Color>? Palette
    {
        get => GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>
    /// The colours used when the palette comes neither from the property nor from the resources.
    /// </summary>
    /// <remarks>
    /// A last resort only: normally the palette comes from the resources under the
    /// <see cref="GraphPalettes.ResourceKey"/> key and changes with the theme (P08-T09).
    /// </remarks>
    public static IReadOnlyList<Color> DefaultPalette => GraphPalettes.LightDefault;

    /// <summary>
    /// The palette in force: property → resource → last resort.
    /// </summary>
    /// <remarks>
    /// Reading it from the resources is mandatory, because the palette depends on <b>both the theme
    /// and the user's colour-blindness preference</b> and either can change at runtime. A fixed list
    /// would leave a user switching to the dark theme with lanes picked against a light background
    /// — some of them all but invisible.
    /// </remarks>
    private IReadOnlyList<Color> EffectivePalette
    {
        get
        {
            if (Palette is { Count: > 0 } custom)
            {
                return custom;
            }

            return this.TryFindResource(GraphPalettes.ResourceKey, ActualThemeVariant, out object? value)
                && value is IReadOnlyList<Color> { Count: > 0 } fromResources
                ? fromResources
                : DefaultPalette;
        }
    }

    /// <summary>
    /// The brush/pen cache, keyed by colour index.
    /// </summary>
    /// <remarks>
    /// Static and shared: because the same palette is used on every row, keeping a per-row cache
    /// would be pointless. The key includes the thickness as well, because a pen depends on its
    /// thickness.
    /// </remarks>
    private static readonly Dictionary<(Color Color, double Thickness), IPen> _penCache = [];
    private static readonly Dictionary<Color, IBrush> _brushCache = [];
    private static readonly Lock _cacheLock = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        // The width depends on the WINDOW, NOT ON THE ROW: every row must be the same width,
        // otherwise the columns shift from row to row and the SHA/subject alignment breaks
        // (measured in P03-T21).
        return new Size(Math.Max(VisibleLanes, 1) * LaneWidth, 0);
    }

    public override void Render(DrawingContext context)
    {
        if (Row is not { } row)
        {
            return;
        }

        IReadOnlyList<Color> palette = EffectivePalette;

        double height = Bounds.Height;
        double centerY = height / 2;
        double laneWidth = LaneWidth;
        double thickness = LineThickness;

        int first = FirstLane;
        int last = first + Math.Max(VisibleLanes, 1) - 1;

        // Edges first, so the nodes are drawn over them.
        foreach (GraphEdge edge in row.Edges)
        {
            // An edge entirely outside the window is not drawn at all. An edge with one end inside
            // is drawn, and clipping (ClipToBounds) keeps it inside the box — so the user can see
            // that the lane carries on outwards.
            if ((edge.FromLane < first && edge.ToLane < first)
                || (edge.FromLane > last && edge.ToLane > last))
            {
                continue;
            }

            IPen pen = GetPen(palette[edge.ColorIndex % palette.Count], thickness);

            double x1 = LaneCenter(edge.FromLane, first, laneWidth);
            double x2 = LaneCenter(edge.ToLane, first, laneWidth);

            // The edge starts at the middle of this row and reaches the middle of the next one.
            // Because the lower bound is the bottom of the row, it joins the next row's line.
            context.DrawLine(pen, new Point(x1, centerY), new Point(x2, centerY + height));
        }

        if (row.Lane >= first && row.Lane <= last)
        {
            context.DrawEllipse(
                GetBrush(palette[row.ColorIndex % palette.Count]),
                null,
                new Point(LaneCenter(row.Lane, first, laneWidth), centerY),
                NodeRadius,
                NodeRadius);
        }

        DrawOverflowMarkers(context, row, first, last, laneWidth, centerY);
    }

    private static double LaneCenter(int lane, int firstLane, double laneWidth) =>
        ((lane - firstLane) * laneWidth) + (laneWidth / 2);

    /// <summary>
    /// Puts a marker at the edge when there are hidden lanes to the left or right of the window.
    /// </summary>
    /// <remarks>
    /// Swallowing a hidden lane silently makes the user think they are seeing the whole graph. The
    /// marker is small and neutrally coloured: it is not data, it is the information that "this
    /// carries on here".
    /// </remarks>
    private void DrawOverflowMarkers(
        DrawingContext context,
        GraphRow row,
        int first,
        int last,
        double laneWidth,
        double centerY)
    {
        bool hiddenLeft = first > 0;
        bool hiddenRight = row.LaneCount > last + 1;

        if (!hiddenLeft && !hiddenRight)
        {
            return;
        }

        IPen marker = GetPen(_overflowColor, 1);
        double inset = laneWidth / 4;

        if (hiddenLeft)
        {
            context.DrawLine(marker, new Point(inset, centerY - 3), new Point(0, centerY));
            context.DrawLine(marker, new Point(0, centerY), new Point(inset, centerY + 3));
        }

        if (hiddenRight)
        {
            double right = Bounds.Width;
            context.DrawLine(marker, new Point(right - inset, centerY - 3), new Point(right, centerY));
            context.DrawLine(marker, new Point(right, centerY), new Point(right - inset, centerY + 3));
        }
    }

    /// <summary>The overflow marker's colour — neutral so it does not blend into the lane palette.</summary>
    private static readonly Color _overflowColor = Color.FromRgb(0x90, 0x90, 0x90);

    private static IPen GetPen(Color color, double thickness)
    {
        lock (_cacheLock)
        {
            if (!_penCache.TryGetValue((color, thickness), out IPen? pen))
            {
                pen = new Pen(GetBrushCore(color), thickness);
                _penCache[(color, thickness)] = pen;
            }

            return pen;
        }
    }

    private static IBrush GetBrush(Color color)
    {
        lock (_cacheLock)
        {
            return GetBrushCore(color);
        }
    }

    private static IBrush GetBrushCore(Color color)
    {
        if (!_brushCache.TryGetValue(color, out IBrush? brush))
        {
            brush = new SolidColorBrush(color);
            _brushCache[color] = brush;
        }

        return brush;
    }
}
