using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GitExt.Graph;

namespace GitExt.UI.Controls;

/// <summary>
/// Tek bir commit satırının grafik sütununu çizer (P03-T10).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden özel çizim?</b> Şerit sayısı satırdan satıra değişir; XAML'de her satır için
/// değişken sayıda <c>Line</c>/<c>Ellipse</c> yaratmak hem pahalı hem hantal olurdu.
/// Grafik sütunu, "hazır kontroller mi özel çizim mi" tartışmasının **zorunlu olarak özel
/// çizim** tarafında kalan tek parçası (P03-T09).
/// </para>
/// <para>
/// Kapsam bilinçli olarak dar: <b>yalnızca</b> şeritleri, düğümü ve kenarları çizer.
/// Satırın geri kalanı (SHA, konu, yazar, tarih, rozetler) sanallaştırılmış <c>ListBox</c>
/// şablonundaki normal kontrollerle çizilir — seçim, klavye gezinme ve erişilebilirlik
/// oradan bedava gelir.
/// </para>
/// <para>
/// <b>Tahsis disiplini:</b> <see cref="IPen"/> ve <see cref="IBrush"/> nesneleri statik olarak
/// önbelleklenir. Her karede yaratmak, 60 FPS'te saniyede binlerce nesne demek olurdu.
/// </para>
/// </remarks>
public sealed class CommitGraphCell : Control
{
    /// <summary>İki şerit merkezi arasındaki yatay mesafe.</summary>
    public static readonly StyledProperty<double> LaneWidthProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(LaneWidth), 14);

    /// <summary>Commit düğümünün yarıçapı.</summary>
    public static readonly StyledProperty<double> NodeRadiusProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(NodeRadius), 4);

    /// <summary>Çizgi kalınlığı.</summary>
    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<CommitGraphCell, double>(nameof(LineThickness), 2);

    /// <summary>Bu satırın yerleşim sonucu.</summary>
    public static readonly StyledProperty<GraphRow?> RowProperty =
        AvaloniaProperty.Register<CommitGraphCell, GraphRow?>(nameof(Row));

    /// <summary>
    /// Şerit renkleri.
    /// </summary>
    /// <remarks>
    /// Varsayılan palet geçicidir; gerçek palet Faz 08'de temaya bağlanacak
    /// (renk körlüğü uyumluluğu dahil). <see cref="GraphRow.ColorIndex"/> bu diziye
    /// modulo ile eşlenir.
    /// </remarks>
    public static readonly StyledProperty<IReadOnlyList<Color>?> PaletteProperty =
        AvaloniaProperty.Register<CommitGraphCell, IReadOnlyList<Color>?>(nameof(Palette));

    static CommitGraphCell()
    {
        AffectsRender<CommitGraphCell>(
            RowProperty, LaneWidthProperty, NodeRadiusProperty, LineThicknessProperty, PaletteProperty);

        AffectsMeasure<CommitGraphCell>(RowProperty, LaneWidthProperty);
    }

    public double LaneWidth
    {
        get => GetValue(LaneWidthProperty);
        set => SetValue(LaneWidthProperty, value);
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
    /// Palet verilmediğinde kullanılan geçici renkler.
    /// </summary>
    /// <remarks>
    /// ⚠️ Bu palet <b>renk körlüğü açısından doğrulanmadı</b>. Faz 08 (P08-T09) bunu
    /// erişilebilir bir paletle değiştirecek.
    /// </remarks>
    public static IReadOnlyList<Color> DefaultPalette { get; } =
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
    /// Renk indeksinden fırça/kalem önbelleği.
    /// </summary>
    /// <remarks>
    /// Statik ve paylaşımlı: aynı palet tüm satırlarda kullanıldığı için satır başına
    /// önbellek tutmak gereksiz olurdu. Anahtar, kalınlığı da içerir çünkü kalem
    /// kalınlığa bağlıdır.
    /// </remarks>
    private static readonly Dictionary<(Color Color, double Thickness), IPen> _penCache = [];
    private static readonly Dictionary<Color, IBrush> _brushCache = [];
    private static readonly Lock _cacheLock = new();

    protected override Size MeasureOverride(Size availableSize)
    {
        // Genişlik, bu satırdaki şerit sayısı kadar. Yükseklik satırın kendisinden gelir.
        int lanes = Row?.LaneCount ?? 1;
        return new Size(Math.Max(lanes, 1) * LaneWidth, 0);
    }

    public override void Render(DrawingContext context)
    {
        if (Row is not { } row)
        {
            return;
        }

        IReadOnlyList<Color> palette = Palette is { Count: > 0 } custom ? custom : DefaultPalette;

        double height = Bounds.Height;
        double centerY = height / 2;
        double laneWidth = LaneWidth;
        double thickness = LineThickness;

        // Kenarlar önce: düğüm üstlerine çizilsin.
        foreach (GraphEdge edge in row.Edges)
        {
            IPen pen = GetPen(palette[edge.ColorIndex % palette.Count], thickness);

            double x1 = (edge.FromLane * laneWidth) + (laneWidth / 2);
            double x2 = (edge.ToLane * laneWidth) + (laneWidth / 2);

            // Kenar bu satırın ortasından başlar ve bir sonraki satırın ortasına uzanır.
            // Alt sınır satırın altı olduğu için bir sonraki satırın çizgisiyle birleşir.
            context.DrawLine(pen, new Point(x1, centerY), new Point(x2, centerY + height));
        }

        // Düğüm.
        context.DrawEllipse(
            GetBrush(palette[row.ColorIndex % palette.Count]),
            null,
            new Point((row.Lane * laneWidth) + (laneWidth / 2), centerY),
            NodeRadius,
            NodeRadius);
    }

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
