using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Neyin neyle karşılaştırıldığı (P04-T16).
/// </summary>
public enum CompareTarget
{
    /// <summary>İki keyfi revizyon arası.</summary>
    Revisions,

    /// <summary>Bir revizyon ile çalışma ağacı arası.</summary>
    WorkingTree,
}

/// <summary>
/// İki revizyonu karşılaştıran <b>ayrı pencerenin</b> ViewModel'i (P04-T16).
/// </summary>
/// <remarks>
/// <para>
/// Pencere <b>modeless</b> ve aynı anda birden fazla açılabilir; her biri kendi
/// <see cref="CompareViewModel"/>'ine sahiptir. Karar GitExtensions'a bakılarak verilmişti:
/// orada da <c>FormDiff</c> <c>ShowDialog</c> ile değil <b><c>Show()</c></b> ile açılıyor.
/// </para>
/// <para>
/// Diff'i göstermek için <see cref="DiffViewModel"/> <b>yeniden kullanılıyor</b> — o bileşen
/// P04-T08'de bilinçli olarak ana pencereden bağımsız yazılmıştı, tam da bu yüzden.
/// </para>
/// </remarks>
public sealed partial class CompareViewModel : ViewModelBase
{
    public CompareViewModel(IDiffReader reader, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        WorkingDirectory = workingDirectory;
        Diff = new DiffViewModel(reader);
    }

    public string WorkingDirectory { get; }

    public DiffViewModel Diff { get; }

    /// <summary>Karşılaştırmanın sol tarafı (temel).</summary>
    [ObservableProperty]
    public partial string FromRevision { get; private set; } = string.Empty;

    /// <summary>Sağ taraf; çalışma ağacı karşılaştırmasında boş.</summary>
    [ObservableProperty]
    public partial string ToRevision { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial CompareTarget Target { get; private set; }

    /// <summary>Pencere başlığı.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = Loc.T("compare.compare");

    /// <summary>İki commit'i karşılaştırır.</summary>
    public Task CompareAsync(
        CommitId from,
        CommitId to,
        CancellationToken cancellationToken = default) =>
        CompareAsync(from.Value, to.Value, cancellationToken);

    /// <summary>İki revizyonu (commit, dal, etiket) karşılaştırır.</summary>
    public Task CompareAsync(
        string from,
        string to,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        FromRevision = from;
        ToRevision = to;
        Target = CompareTarget.Revisions;
        Title = $"{Shorten(from)} ↔ {Shorten(to)}";

        return Diff.ShowRangeAsync(WorkingDirectory, from, to, Title, cancellationToken: cancellationToken);
    }

    /// <summary>Bir revizyonu <b>çalışma ağacıyla</b> karşılaştırır.</summary>
    public Task CompareWithWorkingTreeAsync(
        string from,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);

        FromRevision = from;
        ToRevision = string.Empty;
        Target = CompareTarget.WorkingTree;
        Title = $"{Shorten(from)} ↔ working tree";

        return Diff.ShowRangeAsync(WorkingDirectory, from, null, Title, cancellationToken: cancellationToken);
    }

    /// <summary>Aynı karşılaştırmayı yeniden okur.</summary>
    /// <remarks>
    /// Çalışma ağacı karşılaştırmasında gerekli: kullanıcı dosyayı düzenleyip pencereyi açık
    /// bırakabilir.
    /// </remarks>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        Target == CompareTarget.WorkingTree
            ? CompareWithWorkingTreeAsync(FromRevision, cancellationToken)
            : CompareAsync(FromRevision, ToRevision, cancellationToken);

    /// <summary>SHA'ları kısaltır; dal/etiket adlarına dokunmaz.</summary>
    private static string Shorten(string revision) =>
        revision.Length == 40 && revision.All(Uri.IsHexDigit) ? revision[..8] : revision;
}
