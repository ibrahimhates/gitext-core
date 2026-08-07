using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using GitExt.Core.Model;

namespace GitExt.UI.Controls;

/// <summary>
/// Bir diff satırının parçalarını tek bir <see cref="TextBlock"/> içine çizer (P04-T13).
/// </summary>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ — bu sınıfın var olma sebebi:</b> parçalar önce yatay bir <c>StackPanel</c>
/// içinde ayrı <c>TextBlock</c>'lar olarak çiziliyordu. O düzende <b>satır kaydırma hiç
/// çalışmıyor</b>: yatay panel çocuklarını sonsuz genişlikte ölçüyor, metin hiç sarılmıyor
/// (ölçüm: sarmalı uzun satır 17 px, yani tek satır). Aynı satır tek bir <c>TextBlock</c>'a
/// <c>Run</c> olarak konduğunda 170 px, yani <b>on satıra sarılıyor</b> — ve
/// <c>Run.Background</c> destekleniyor, dolayısıyla satır içi vurgulama korunuyor.
/// </para>
/// <para>
/// Yan etkisi olumlu: satır başına kontrol sayısı düşüyor (parça başına
/// <c>Border</c>+<c>TextBlock</c> yerine tek <c>TextBlock</c>).
/// </para>
/// <para>
/// Renkler <b>tema kaynak sözlüğünden</b> geliyor (P08-T07): <c>GitExtDiffAddedWordBrush</c>
/// ve <c>GitExtDiffRemovedWordBrush</c>. Sabit yazılsalardı koyu temada okunmazlardı —
/// satır arka planları temayla değişip satır içi vurgular değişmeyince, vurgu zeminden
/// ayırt edilemez hâle gelirdi.
/// </para>
/// </remarks>
public static class DiffTextPresenter
{
    private const string AddedBrushKey = "GitExtDiffAddedWordBrush";
    private const string RemovedBrushKey = "GitExtDiffRemovedWordBrush";

    /// <summary>Çizilecek parçalar.</summary>
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

        // Tek parçalı satır yaygın durum (satır içi fark yoksa): `Run` üretmeden doğrudan
        // metin veriliyor.
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
    /// Fırçayı kontrolün <b>yürürlükteki temasına</b> göre çözer.
    /// </summary>
    /// <remarks>
    /// <c>ActualThemeVariant</c> ile sorulmak zorunda: P08-T00/M07b'de ölçüldü, uygulama
    /// "sistemi takip et" modundayken bile bu değer <b>somut</b> bir varyanta çözülüyor,
    /// dolayısıyla doğru sözlüğe bakılıyor. Anahtar bulunamazsa <see langword="null"/> —
    /// vurgusuz ama okunur bir satır, yanlış renkli bir satırdan iyidir.
    /// </remarks>
    private static IBrush? Resolve(TextBlock target, string key) =>
        target.TryFindResource(key, target.ActualThemeVariant, out object? value)
            ? value as IBrush
            : null;
}
