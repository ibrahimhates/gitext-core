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
    /// The row above — the lines that come down into this row belong to it (P12-T09).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The graph was broken without this.</b> Drawing is clipped to each row's own box, so a
    /// row that only draws the lines LEAVING it downwards leaves the upper half of every
    /// connection unpainted: the lanes look cut off above every commit. GitExtensions draws each
    /// segment through three points — previous centre, this centre, next centre — so each half is
    /// drawn by the row it belongs to and the clipped ends meet.
    /// </remarks>
    public static readonly StyledProperty<GraphRow?> PreviousRowProperty =
        AvaloniaProperty.Register<CommitGraphCell, GraphRow?>(nameof(PreviousRow));

    /// <summary>Does the commit carry refs? Then its node is a square, as in GitExtensions.</summary>
    public static readonly StyledProperty<bool> HasRefsProperty =
        AvaloniaProperty.Register<CommitGraphCell, bool>(nameof(HasRefs));

    /// <summary>Is this the commit <c>HEAD</c> points at? Then its node gets an outline.</summary>
    public static readonly StyledProperty<bool> IsHeadProperty =
        AvaloniaProperty.Register<CommitGraphCell, bool>(nameof(IsHead));

    /// <summary>Is the commit an ancestor of <c>HEAD</c>? If not, it is drawn grey.</summary>
    public static readonly StyledProperty<bool> IsRelativeProperty =
        AvaloniaProperty.Register<CommitGraphCell, bool>(nameof(IsRelative), true);

    /// <summary>Is the row above relative? It colours the lines coming down from it.</summary>
    public static readonly StyledProperty<bool> PreviousIsRelativeProperty =
        AvaloniaProperty.Register<CommitGraphCell, bool>(nameof(PreviousIsRelative), true);

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
            RowProperty, PreviousRowProperty, LaneWidthProperty, NodeRadiusProperty,
            LineThicknessProperty, PaletteProperty, FirstLaneProperty, VisibleLanesProperty,
            HasRefsProperty, IsHeadProperty, IsRelativeProperty, PreviousIsRelativeProperty);

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

    public GraphRow? PreviousRow
    {
        get => GetValue(PreviousRowProperty);
        set => SetValue(PreviousRowProperty, value);
    }

    public bool HasRefs
    {
        get => GetValue(HasRefsProperty);
        set => SetValue(HasRefsProperty, value);
    }

    public bool IsHead
    {
        get => GetValue(IsHeadProperty);
        set => SetValue(IsHeadProperty, value);
    }

    public bool IsRelative
    {
        get => GetValue(IsRelativeProperty);
        set => SetValue(IsRelativeProperty, value);
    }

    public bool PreviousIsRelative
    {
        get => GetValue(PreviousIsRelativeProperty);
        set => SetValue(PreviousIsRelativeProperty, value);
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

        // 🔴 The lines COMING DOWN from the row above are drawn first, and they are what used to
        // be missing: drawing is clipped to this row's box, so the half of the connection above
        // this commit belongs to nobody unless it is drawn here. Without it every lane looked cut
        // off above every commit — the whole graph read as a column of dashes.
        if (PreviousRow is { } previous)
        {
            foreach (GraphEdge edge in previous.Edges)
            {
                DrawSegment(
                    context,
                    palette,
                    edge,
                    PreviousIsRelative,
                    thickness,
                    laneWidth,
                    first,
                    last,
                    fromY: centerY - height,
                    toY: centerY);
            }
        }

        // …and the lines leaving this row downwards. The two halves are drawn by the two rows
        // they belong to and meet exactly on the boundary.
        foreach (GraphEdge edge in row.Edges)
        {
            DrawSegment(
                context,
                palette,
                edge,
                IsRelative,
                thickness,
                laneWidth,
                first,
                last,
                fromY: centerY,
                toY: centerY + height);
        }

        if (row.Lane >= first && row.Lane <= last)
        {
            DrawNode(context, row, palette, first, laneWidth, centerY);
        }

        DrawOverflowMarkers(context, row, first, last, laneWidth, centerY);
    }

    /// <summary>
    /// Draws one half of a connection.
    /// </summary>
    /// <remarks>
    /// A lane that does not change is a straight line. A lane change is a <b>Bezier curve</b> whose
    /// control points sit at the vertical midpoint — that is GitExtensions' "curvy" rendering
    /// (<c>SegmentRenderer</c>), and it is what makes a branch leave its parent smoothly instead of
    /// with the kink a straight diagonal produces.
    /// </remarks>
    private void DrawSegment(
        DrawingContext context,
        IReadOnlyList<Color> palette,
        GraphEdge edge,
        bool relative,
        double thickness,
        double laneWidth,
        int firstLane,
        int lastLane,
        double fromY,
        double toY)
    {
        // A segment entirely outside the lane window is not drawn at all. One with an end inside
        // is drawn and clipped, so the user can see that the lane carries on outwards.
        if ((edge.FromLane < firstLane && edge.ToLane < firstLane)
            || (edge.FromLane > lastLane && edge.ToLane > lastLane))
        {
            return;
        }

        IPen pen = GetPen(SegmentColor(palette, edge, relative), thickness);

        double x1 = LaneCenter(edge.FromLane, firstLane, laneWidth);
        double x2 = LaneCenter(edge.ToLane, firstLane, laneWidth);

        if (Math.Abs(x1 - x2) < 0.01)
        {
            context.DrawLine(pen, new Point(x1, fromY), new Point(x2, toY));
            return;
        }

        double midY = (fromY + toY) / 2;

        StreamGeometry geometry = new();

        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(x1, fromY), isFilled: false);
            path.CubicBezierTo(new Point(x1, midY), new Point(x2, midY), new Point(x2, toY));
            path.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// Draws the commit's node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shapes are GitExtensions': a <b>circle</b> for an ordinary commit, a <b>square</b> when
    /// the commit carries refs (a branch tip or a tag), and an <b>outline</b> around the node
    /// <c>HEAD</c> points at.
    /// </para>
    /// <para>
    /// So every commit keeps a node — a commit without one could not be told from an empty row,
    /// and every row in this list is a commit that can be selected and checked out. What the shape
    /// says is not "you may click here" but "this one is a tip, and this one is where you are".
    /// </para>
    /// </remarks>
    private void DrawNode(
        DrawingContext context,
        GraphRow row,
        IReadOnlyList<Color> palette,
        int firstLane,
        double laneWidth,
        double centerY)
    {
        double x = LaneCenter(row.Lane, firstLane, laneWidth);
        double radius = NodeRadius;
        IBrush brush = GetBrush(NodeColor(palette, row, IsRelative));

        if (HasRefs)
        {
            context.FillRectangle(brush, new Rect(x - radius, centerY - radius, radius * 2, radius * 2));
        }
        else
        {
            context.DrawEllipse(brush, null, new Point(x, centerY), radius, radius);
        }

        if (!IsHead)
        {
            return;
        }

        // The outline is drawn in the foreground colour rather than the lane colour: it has to be
        // visible against the node, and the node is already the lane colour.
        IPen outline = GetPen(OutlineColor, 1.4);
        double outer = radius + 1.5;

        if (HasRefs)
        {
            context.DrawRectangle(null, outline, new Rect(x - outer, centerY - outer, outer * 2, outer * 2));
        }
        else
        {
            context.DrawEllipse(null, outline, new Point(x, centerY), outer, outer);
        }
    }

    /// <summary>
    /// The colour of a segment: the lane's own, or grey when it is not part of HEAD's history.
    /// </summary>
    /// <remarks>
    /// GitExtensions calls this <c>DrawNonRelativesGray</c>. It is the answer to "which branch am
    /// I on": the history you are actually on keeps its colours and everything else steps back.
    /// </remarks>
    private static Color SegmentColor(IReadOnlyList<Color> palette, GraphEdge edge, bool relative) =>
        relative ? palette[edge.ColorIndex % palette.Count] : NonRelativeColor;

    private static Color NodeColor(IReadOnlyList<Color> palette, GraphRow row, bool relative) =>
        relative ? palette[row.ColorIndex % palette.Count] : NonRelativeColor;

    /// <summary>The colour of the lanes that are not part of HEAD's history.</summary>
    private static readonly Color NonRelativeColor = Color.FromRgb(0xA0, 0xA0, 0xA0);

    /// <summary>The colour of the outline around the HEAD node.</summary>
    private Color OutlineColor =>
        ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? Color.FromRgb(0xE6, 0xED, 0xF3)
            : Color.FromRgb(0x1E, 0x1E, 0x1E);

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
