using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using GitExt.Core.Model;

namespace GitExt.UI.Controls;

/// <summary>
/// Draws a diff line's segments inside a single <see cref="TextBlock"/> (P04-T13).
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED — why this class exists:</b> the segments used to be drawn as separate
/// <c>TextBlock</c>s inside a horizontal <c>StackPanel</c>. In that arrangement <b>word wrap does not
/// work at all</b>: a horizontal panel measures its children at infinite width and the text never
/// wraps (measured: a long line with wrapping on came to 17 px, that is, a single line). Put into a
/// single <c>TextBlock</c> as <c>Run</c>s, the same line comes to 170 px, meaning it <b>wraps over ten
/// lines</b> — and <c>Run.Background</c> is supported, so the intra-line highlighting is preserved.
/// </para>
/// <para>
/// The side effect is a positive one: the control count per row drops (a single <c>TextBlock</c>
/// instead of a <c>Border</c>+<c>TextBlock</c> per segment).
/// </para>
/// <para>
/// The colours come from the <b>theme resource dictionary</b> (P08-T07):
/// <c>GitExtDiffAddedWordBrush</c> and <c>GitExtDiffRemovedWordBrush</c>. Hard-coded they would be
/// illegible in the dark theme — with the line backgrounds changing with the theme and the intra-line
/// highlights not, the highlight would become indistinguishable from its background.
/// </para>
/// </remarks>
public static class DiffTextPresenter
{
    private const string AddedBrushKey = "GitExtDiffAddedWordBrush";
    private const string RemovedBrushKey = "GitExtDiffRemovedWordBrush";

    /// <summary>The segments to draw.</summary>
    public static readonly AttachedProperty<IReadOnlyList<DiffSegment>?> SegmentsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<DiffSegment>?>(
            "Segments",
            typeof(DiffTextPresenter));

    static DiffTextPresenter()
    {
        SegmentsProperty.Changed.AddClassHandler<TextBlock>(OnSegmentsChanged);
    }

    public static IReadOnlyList<DiffSegment>? GetSegments(TextBlock target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(SegmentsProperty);
    }

    public static void SetSegments(TextBlock target, IReadOnlyList<DiffSegment>? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(SegmentsProperty, value);
    }

    private static void OnSegmentsChanged(TextBlock target, AvaloniaPropertyChangedEventArgs args)
    {
        IReadOnlyList<DiffSegment>? segments = args.GetNewValue<IReadOnlyList<DiffSegment>?>();

        InlineCollection inlines = target.Inlines ??= [];
        inlines.Clear();

        if (segments is null || segments.Count == 0)
        {
            return;
        }

        // A single-segment line is the common case (when there is no intra-line difference): the text is
        // given directly without producing a `Run`.
        if (segments.Count == 1 && segments[0].Kind == DiffLineKind.Context)
        {
            target.Text = segments[0].Text;
            return;
        }

        foreach (DiffSegment segment in segments)
        {
            inlines.Add(new Run(segment.Text)
            {
                Background = segment.Kind switch
                {
                    DiffLineKind.Added => Resolve(target, AddedBrushKey),
                    DiffLineKind.Removed => Resolve(target, RemovedBrushKey),
                    _ => null,
                },
            });
        }
    }

    /// <summary>
    /// Resolves the brush against the control's <b>theme in force</b>.
    /// </summary>
    /// <remarks>
    /// It has to be asked for with <c>ActualThemeVariant</c>: measured in P08-T00/M07b, this value
    /// resolves to a <b>concrete</b> variant even while the application is in "follow the system" mode,
    /// so the right dictionary is consulted. When the key cannot be found, <see langword="null"/> — an
    /// unhighlighted but readable line beats a line in the wrong colour.
    /// </remarks>
    private static IBrush? Resolve(TextBlock target, string key) =>
        target.TryFindResource(key, target.ActualThemeVariant, out object? value)
            ? value as IBrush
            : null;
}
