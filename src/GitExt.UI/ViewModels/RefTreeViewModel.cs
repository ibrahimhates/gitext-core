using System.Collections.ObjectModel;
using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>Ağaçtaki bir düğümün türü (P06-T13).</summary>
public enum RefNodeKind
{
    /// <summary>Üst başlık: <i>Dallar</i>, <i>Uzak depolar</i>, <i>Etiketler</i>.</summary>
    Section,

    /// <summary>Ad içindeki <c>/</c>'lardan doğan klasör (<c>feature/</c>).</summary>
    Folder,

    /// <summary>Yerel dal.</summary>
    LocalBranch,

    /// <summary>Uzak dal.</summary>
    RemoteBranch,

    /// <summary>Uzak depo (uzak dalların üstünde).</summary>
    Remote,

    /// <summary>Etiket.</summary>
    Tag,
}

/// <summary>
/// Dal panelindeki bir düğüm (P06-T13).
/// </summary>
/// <remarks>
/// Adlar GitExtensions'ın <c>RepoObjectsTree</c>'sindeki gibi <c>/</c>'lardan bölünüp
/// klasörleniyor: <c>feature/login</c> → <i>feature</i> ▸ <i>login</i>. Onlarca dalı düz
/// bir liste olarak göstermek, tam da panelin çözmesi gereken sorunu geri getirirdi.
/// </remarks>
public sealed class RefNodeViewModel : ViewModelBase
{
    private bool _isExpanded = true;

    public required string Name { get; init; }

    /// <summary>Tam ref adı (<c>main</c>, <c>origin/main</c>, <c>v1.0</c>); klasörde boş.</summary>
    public string FullName { get; init; } = string.Empty;

    public required RefNodeKind Kind { get; init; }

    /// <summary>Bu dal şu an checkout edilmiş mi?</summary>
    public bool IsCurrent { get; init; }

    /// <summary>Upstream'e göre konum; yoksa boş.</summary>
    public string AheadBehind { get; init; } = string.Empty;

    public bool HasAheadBehind => AheadBehind.Length > 0;

    /// <summary>Üzerine çift tıklanabilir mi (checkout)?</summary>
    public bool IsCheckoutable => Kind is RefNodeKind.LocalBranch or RefNodeKind.RemoteBranch;

    public ObservableCollection<RefNodeViewModel> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public override string ToString() => FullName.Length > 0 ? FullName : Name;
}

/// <summary>
/// Dal paneli (P06-T13).
/// </summary>
/// <remarks>
/// <para>
/// Yerleşim GitExtensions'ın sol <c>RepoObjectsTree</c>'sinden (§ 9): üstte <i>Dallar</i>,
/// altında <i>Uzak depolar</i> (her uzak kendi düğümü), en altta <i>Etiketler</i>.
/// </para>
/// <para>
/// 🔑 <b>Veri buradan okunmuyor, veriliyor.</b> Panel <c>RepositoryRefs</c>'i olduğu gibi
/// alıyor — aynı soruyu ikinci bir kod yoluyla sormak, bu projede daha önce iki kez
/// sessizce farklı cevaplar üretmişti (P06-T05'te <c>RefReader</c>, P06-T06'da ref anlık
/// görüntüsü).
/// </para>
/// </remarks>
public sealed class RefTreeViewModel : ViewModelBase
{
    private RepositoryRefs? _refs;
    private string _filter = string.Empty;
    private RefNodeViewModel? _selected;

    /// <summary>Ağacın kökleri.</summary>
    public ObservableCollection<RefNodeViewModel> Roots { get; } = [];

    /// <summary>Arama metni; boşsa her şey görünür.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                Rebuild();
            }
        }
    }

    public RefNodeViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                OnPropertyChanged(nameof(CanCheckoutSelected));
                OnPropertyChanged(nameof(CanMergeSelected));
                OnPropertyChanged(nameof(CanRenameSelected));
                OnPropertyChanged(nameof(CanDeleteSelected));
                OnPropertyChanged(nameof(CanPushSelected));
                OnPropertyChanged(nameof(CanCopySelectedName));
                OnPropertyChanged(nameof(CanBranchFromSelected));
            }
        }
    }

    // ---- Bağlam menüsünün kararları (P06-T14).
    //
    // 🔑 Kararlar BURADA. İlk uygulama `IsEnabled`i menünün `Opening` olayında elle
    // ayarlıyordu; ölçüldü — headless'ta o olay hiç tetiklenmiyor, yani menü sessizce
    // her şeyi etkin gösteriyordu ve test bunu ancak menüyü açmaya çalışınca yakaladı.
    // Faz 03'ün dersinin aynısı: karar görünüm tarafında saklanırsa doğrulanamıyor.

    /// <summary>Seçili düğüme geçilebilir mi?</summary>
    public bool CanCheckoutSelected => Selected?.IsCheckoutable == true;

    /// <summary>Seçili dal mevcut dala birleştirilebilir mi?</summary>
    /// <remarks>Kendini kendine birleştirmek git'in de reddettiği bir şey.</remarks>
    public bool CanMergeSelected => Selected is { IsCheckoutable: true, IsCurrent: false };

    /// <summary>Yeniden adlandırılabilir mi?</summary>
    /// <remarks><c>git branch -m</c> uzak dalı değiştirmez; sunmak yanlış bir vaat olurdu.</remarks>
    public bool CanRenameSelected => Selected?.Kind == RefNodeKind.LocalBranch;

    /// <summary>Silinebilir mi?</summary>
    public bool CanDeleteSelected =>
        Selected is { Kind: RefNodeKind.LocalBranch, IsCurrent: false };

    /// <summary>Gönderilebilir mi?</summary>
    public bool CanPushSelected => Selected?.Kind == RefNodeKind.LocalBranch;

    /// <summary>Adı kopyalanabilir mi? (Etiket de dahil; klasör ve başlık hariç.)</summary>
    public bool CanCopySelectedName => Selected is { FullName.Length: > 0 };

    /// <summary>Buradan yeni dal oluşturulabilir mi?</summary>
    public bool CanBranchFromSelected => Selected is { FullName.Length: > 0 };

    /// <summary>Süzme sonucu hiçbir şey kalmadı mı?</summary>
    public bool IsEmpty => Roots.Count == 0;

    /// <summary>Panel doldurulmuş mu?</summary>
    public bool HasRefs => _refs is not null;

    /// <summary>Ref'leri verir ve ağacı kurar.</summary>
    public void Load(RepositoryRefs? refs)
    {
        _refs = refs;
        Rebuild();
        OnPropertyChanged(nameof(HasRefs));
    }

    private void Rebuild()
    {
        Roots.Clear();

        if (_refs is not { } refs)
        {
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        RefNodeViewModel branches = new() { Name = "Branches", Kind = RefNodeKind.Section };

        foreach (BranchInfo branch in refs.LocalBranches)
        {
            if (!Matches(branch.Name))
            {
                continue;
            }

            Add(
                branches,
                branch.Name,
                new RefNodeViewModel
                {
                    Name = LastSegment(branch.Name),
                    FullName = branch.Name,
                    Kind = RefNodeKind.LocalBranch,
                    IsCurrent = branch.IsCurrent,
                    AheadBehind = Describe(branch.Tracking, branch.Upstream),
                });
        }

        RefNodeViewModel remotes = new() { Name = "Remotes", Kind = RefNodeKind.Section };

        foreach (BranchInfo branch in refs.RemoteBranches)
        {
            // Sembolik `origin/HEAD` atlanıyor: aynı commit'te ikinci bir "dal" gibi
            // görünürdü. Bu projede aynı ref beşinci kez tuzak kuruyor.
            if (branch.Ref.IsSymbolic || !Matches(branch.Name))
            {
                continue;
            }

            int slash = branch.Name.IndexOf('/', StringComparison.Ordinal);

            if (slash <= 0)
            {
                continue;
            }

            string remoteName = branch.Name[..slash];
            string rest = branch.Name[(slash + 1)..];

            RefNodeViewModel remote = Section(remotes, remoteName, RefNodeKind.Remote);

            Add(
                remote,
                rest,
                new RefNodeViewModel
                {
                    Name = LastSegment(rest),
                    FullName = branch.Name,
                    Kind = RefNodeKind.RemoteBranch,
                });
        }

        RefNodeViewModel tags = new() { Name = "Tags", Kind = RefNodeKind.Section };

        foreach (TagInfo tag in refs.Tags)
        {
            if (Matches(tag.Name))
            {
                Add(
                    tags,
                    tag.Name,
                    new RefNodeViewModel
                    {
                        Name = LastSegment(tag.Name),
                        FullName = tag.Name,
                        Kind = RefNodeKind.Tag,
                    });
            }
        }

        // Boş bölüm gösterilmiyor: süzerken "Etiketler" başlığının altında hiçbir şey
        // olmaması kullanıcıya bir şey söylemez.
        foreach (RefNodeViewModel section in new[] { branches, remotes, tags })
        {
            if (section.Children.Count > 0)
            {
                Roots.Add(section);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool Matches(string name) =>
        Filter.Length == 0 || name.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    private static string LastSegment(string name)
    {
        int slash = name.LastIndexOf('/');

        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    /// <summary>Adı <c>/</c>'lardan bölerek klasörleyip yaprağı yerleştirir.</summary>
    private static void Add(RefNodeViewModel parent, string path, RefNodeViewModel leaf)
    {
        string[] segments = path.Split('/');
        RefNodeViewModel current = parent;

        for (int index = 0; index < segments.Length - 1; index++)
        {
            current = Section(current, segments[index], RefNodeKind.Folder);
        }

        current.Children.Add(leaf);
    }

    private static RefNodeViewModel Section(RefNodeViewModel parent, string name, RefNodeKind kind)
    {
        RefNodeViewModel? existing = parent.Children
            .FirstOrDefault(child => child.Kind == kind
                && string.Equals(child.Name, name, StringComparison.Ordinal));

        if (existing is not null)
        {
            return existing;
        }

        RefNodeViewModel created = new() { Name = name, Kind = kind };
        parent.Children.Add(created);

        return created;
    }

    /// <summary>
    /// Ahead/behind rozeti.
    /// </summary>
    /// <remarks>
    /// <c>[gone]</c> ayrı yazılıyor: upstream silinmiş bir dalda "0/0" göstermek, dalın
    /// güncel olduğunu düşündürürdü.
    /// </remarks>
    internal static string Describe(UpstreamTracking tracking, string? upstream)
    {
        if (upstream is not { Length: > 0 })
        {
            return string.Empty;
        }

        if (tracking.IsGone)
        {
            return "upstream yok";
        }

        return tracking.IsUpToDate ? string.Empty : $"↑{tracking.Ahead} ↓{tracking.Behind}";
    }
}
