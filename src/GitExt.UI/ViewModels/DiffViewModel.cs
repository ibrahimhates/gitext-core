using System.Collections.ObjectModel;
using System.Text;
using System.Globalization;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Değişen dosyalar listesindeki tek satır (P04-T08).
/// </summary>
public sealed class FileChangeRow
{
    public FileChangeRow(FileDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        Diff = diff;
    }

    public FileDiff Diff { get; }

    public RepositoryPath Path => Diff.Path;

    /// <summary>Dosya adı — düz listede satırın başlığı.</summary>
    public string Name => Diff.Path.Name;

    /// <summary>Dosyanın bulunduğu klasör; kökteyse boş.</summary>
    public string Directory => Diff.Path.Parent.Value;

    /// <summary>
    /// Durumu tek harfle gösterir (<c>A</c>, <c>M</c>, <c>D</c>, <c>R</c>, <c>C</c>, <c>T</c>).
    /// </summary>
    /// <remarks>
    /// git'in <c>--raw</c> çıktısındaki harflerle aynı: kullanıcı komut satırında gördüğü
    /// gösterimi burada da tanır.
    /// </remarks>
    public string StatusLetter => Diff.Change switch
    {
        FileChangeKind.Added => "A",
        FileChangeKind.Modified => "M",
        FileChangeKind.Deleted => "D",
        FileChangeKind.Renamed => "R",
        FileChangeKind.Copied => "C",
        FileChangeKind.TypeChanged => "T",
        FileChangeKind.Unmerged => "U",
        _ => " ",
    };

    public bool IsAdded => Diff.Change == FileChangeKind.Added;

    public bool IsDeleted => Diff.Change == FileChangeKind.Deleted;

    public bool IsRenamed => Diff.Change is FileChangeKind.Renamed or FileChangeKind.Copied;

    public bool IsBinary => Diff.IsBinary;

    public bool IsTooLarge => Diff.IsTooLarge;

    /// <summary>
    /// Satır sayıları; binary dosyada gösterilmez.
    /// </summary>
    /// <remarks>
    /// Binary dosyada <c>--numstat</c> sayı vermiyor (ölçüldü: <c>-</c> geliyor). "0 / 0"
    /// göstermek "hiç değişmedi" demek olurdu.
    /// </remarks>
    public bool HasLineCounts => !Diff.IsBinary && Diff.StatAdded is not null;

    public int AddedLines => Diff.AddedLines;

    public int RemovedLines => Diff.RemovedLines;

    /// <summary>Yeniden adlandırmada eski yol; yoksa boş.</summary>
    public string RenamedFrom => Diff.OldPath?.Value ?? string.Empty;

    public override string ToString() => $"{StatusLetter} {Path}";
}

/// <summary>
/// Unified diff görünümündeki tek satır (P04-T09).
/// </summary>
/// <remarks>
/// Hunk başlıkları da satır olarak akıyor: tek düz liste sanallaştırma için gerekli —
/// hunk'ları ayrı gruplar hâlinde göstermek satır başına kontrol yaratmak demek olurdu.
/// </remarks>
public sealed class DiffLineRow
{
    private DiffLineRow(string text, DiffLineKind kind)
    {
        Text = text;
        Kind = kind;
    }

    /// <summary>Hunk başlığı satırı üretir.</summary>
    /// <remarks>
    /// Başlık da <b>parça olarak</b> veriliyor: görünüm satırı her zaman
    /// <see cref="Segments"/> üzerinden çiziyor, boş bırakılırsa başlık metni ekranda
    /// hiç görünmez (gerçek depo render'ında bu şekilde yakalandı).
    /// </remarks>
    public static DiffLineRow ForHunk(DiffHunk hunk, int hunkIndex = -1) =>
        new(hunk.Header, DiffLineKind.Context)
        {
            IsHunkHeader = true,
            HunkIndex = hunkIndex,
            Segments = [new DiffSegment(DiffLineKind.Context, hunk.Header)],
        };

    /// <summary>İçerik satırı üretir.</summary>
    /// <param name="line">Model satırı.</param>
    /// <param name="text">
    /// Gösterim ayarları (sekme genişliği, boşluk gösterimi). Model içeriği
    /// <b>değişmez</b> — yalnızca ekranda görünen dönüşür.
    /// </param>
    /// <param name="hunkIndex">Satırın ait olduğu hunk'ın indeksi.</param>
    /// <param name="lineIndex">Satırın hunk içindeki indeksi.</param>
    public static DiffLineRow ForLine(
        DiffLine line,
        DiffTextOptions? text = null,
        int hunkIndex = -1,
        int lineIndex = -1)
    {
        text ??= DiffTextOptions.Default;

        string raw = Display(line.Content);

        return new DiffLineRow(DiffTextFormatter.Format(raw, text), line.Kind)
        {
            RawText = raw,
            HunkIndex = hunkIndex,
            LineIndex = lineIndex,
            OldLineNumber = line.OldLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            NewLineNumber = line.NewLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            EndsWithoutNewline = line.EndsWithoutNewline,
            Segments = DiffTextFormatter.Format(BuildSegments(line), text),
        };
    }

    /// <summary>
    /// Yan yana görünümde karşılığı olmayan tarafa konan dolgu satırı (P04-T10).
    /// </summary>
    /// <remarks>
    /// Dolgu <b>boş satır değildir</b>: "burada bir satır yok" demektir ve arayüzde farklı
    /// boyanır. Boş bir bağlam satırıyla karıştırılırsa kullanıcı olmayan bir satırı var
    /// sanar.
    /// </remarks>
    public static DiffLineRow Filler { get; } =
        new(string.Empty, DiffLineKind.Context) { IsFiller = true };

    /// <summary>Ekranda görünen metin: sekmeler açılmış, istenirse boşluklar işaretlenmiş.</summary>
    public string Text { get; }

    /// <summary>
    /// Kopyalanacak metin — <b>gösterim dönüşümü uygulanmamış</b> hâli.
    /// </summary>
    /// <remarks>
    /// ⚠️ Ayrı tutulmasının sebebi somut: boşluk gösterimi açıkken <see cref="Text"/> içinde
    /// <c>·</c> ve <c>»</c> karakterleri var. Kopyalama onu kullansaydı kullanıcı panoya
    /// <b>bozuk kod</b> almış olurdu; sekme de boşluklara dönüşmüş olurdu.
    /// </remarks>
    public string RawText { get; private init; } = string.Empty;

    public DiffLineKind Kind { get; }

    public bool IsHunkHeader { get; private init; }

    /// <summary>
    /// Satırın ait olduğu hunk'ın indeksi; bilinmiyorsa <c>-1</c>.
    /// </summary>
    /// <remarks>
    /// Kısmi staging (P05-T10) seçili satırları <see cref="PatchSelection"/>'a çevirirken
    /// bunu kullanıyor. Ekrandaki satır sırası <b>yeterli değil</b>: hunk başlıkları da
    /// listede duruyor ve indeksleri kaydırıyor.
    /// </remarks>
    public int HunkIndex { get; private init; } = -1;

    /// <summary>Satırın hunk içindeki indeksi; başlık ve dolgu satırlarında <c>-1</c>.</summary>
    public int LineIndex { get; private init; } = -1;

    public bool IsFiller { get; private init; }

    public string OldLineNumber { get; private init; } = string.Empty;

    public string NewLineNumber { get; private init; } = string.Empty;

    public bool EndsWithoutNewline { get; private init; }

    /// <summary>
    /// Satırın parçaları; satır içi fark hesaplanmadıysa tek parça.
    /// </summary>
    /// <remarks>
    /// Görünüm her zaman parçalar üzerinden çiziyor: "parça var mı" ayrımını arayüzde
    /// yapmak iki ayrı şablon bakımı demek olurdu.
    /// </remarks>
    public IReadOnlyList<DiffSegment> Segments { get; private init; } = [];

    public bool IsAdded => !IsHunkHeader && Kind == DiffLineKind.Added;

    public bool IsRemoved => !IsHunkHeader && Kind == DiffLineKind.Removed;

    /// <summary>
    /// Gösterim metni: sondaki <c>\r</c> kırpılır.
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ (P04-T07):</b> CRLF dosyada satır içeriği <c>\r</c> ile bitiyor ve model
    /// bunu <b>bilerek koruyor</b> (Faz 05'te yamayı <c>git apply</c>'a birebir geri vermek
    /// için gerekli). Ekranda ise kutu karakteri olarak görünürdü.
    /// </remarks>
    private static string Display(string content) =>
        content.EndsWith('\r') ? content[..^1] : content;

    private static IReadOnlyList<DiffSegment> BuildSegments(DiffLine line)
    {
        if (line.Segments.Count == 0)
        {
            return [new DiffSegment(DiffLineKind.Context, Display(line.Content))];
        }

        DiffSegment[] segments = [.. line.Segments];

        // Sondaki `\r` yalnızca son parçada olabilir.
        int last = segments.Length - 1;

        if (segments[last].Text.EndsWith('\r'))
        {
            segments[last] = segments[last] with { Text = segments[last].Text[..^1] };
        }

        return segments;
    }

    public override string ToString() => IsHunkHeader ? Text : $"{Kind} {Text}";
}

/// <summary>
/// Diff'in panoya hangi biçimde kopyalanacağı (P04-T12).
/// </summary>
/// <remarks>
/// Dört mod da <b>GitExtensions'ın dört kopyalama komutunun</b> karşılığı: <i>Copy</i>
/// (öneksiz kod), <i>Copy patch</i>, <i>Copy old version</i>, <i>Copy new version</i>.
/// </remarks>
public enum DiffCopyMode
{
    /// <summary>Yalnızca kod: <c>+</c>/<c>-</c> önekleri ve hunk başlıkları yok. Varsayılan.</summary>
    Code,

    /// <summary>Yama biçimi: hunk başlıkları ve <c>+</c>/<c>-</c>/boşluk önekleriyle.</summary>
    Patch,

    /// <summary>Dosyanın eski hâli: eklenen satırlar atlanır.</summary>
    OldVersion,

    /// <summary>Dosyanın yeni hâli: silinen satırlar atlanır.</summary>
    NewVersion,
}

/// <summary>
/// Yan yana görünümdeki tek satır: solda eski, sağda yeni (P04-T10).
/// </summary>
/// <remarks>
/// İki taraf da <b>her zaman doludur</b>; karşılığı olmayan tarafa
/// <see cref="DiffLineRow.Filler"/> konur. Böylece şablonda <c>null</c> kontrolü gerekmiyor
/// ve dolgu ayrı boyanabiliyor.
/// </remarks>
public sealed class SideBySideLineRow
{
    private SideBySideLineRow(DiffLineRow left, DiffLineRow right)
    {
        Left = left;
        Right = right;
    }

    public static SideBySideLineRow ForHunk(string header, int hunkIndex = -1) =>
        new(DiffLineRow.Filler, DiffLineRow.Filler)
        {
            IsHunkHeader = true,
            Header = header,
            HunkIndex = hunkIndex,
        };

    public static SideBySideLineRow ForRow(SideBySideRow row, DiffTextOptions? text = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new SideBySideLineRow(
            row.Left is null
                ? DiffLineRow.Filler
                : DiffLineRow.ForLine(row.Left, text, row.HunkIndex, row.LeftIndex),
            row.Right is null
                ? DiffLineRow.Filler
                : DiffLineRow.ForLine(row.Right, text, row.HunkIndex, row.RightIndex));
    }

    public DiffLineRow Left { get; }

    public DiffLineRow Right { get; }

    public bool IsHunkHeader { get; private init; }

    /// <summary>
    /// Satırın ait olduğu hunk'ın indeksi; bilinmiyorsa <c>-1</c>.
    /// </summary>
    /// <remarks>
    /// Kısmi staging (P05-T10) seçili satırları <see cref="PatchSelection"/>'a çevirirken
    /// bunu kullanıyor. Ekrandaki satır sırası <b>yeterli değil</b>: hunk başlıkları da
    /// listede duruyor ve indeksleri kaydırıyor.
    /// </remarks>
    public int HunkIndex { get; private init; } = -1;

    /// <summary>Satırın hunk içindeki indeksi; başlık ve dolgu satırlarında <c>-1</c>.</summary>
    public int LineIndex { get; private init; } = -1;

    public string Header { get; private init; } = string.Empty;

    public override string ToString() =>
        IsHunkHeader ? Header : $"{Left.Text} │ {Right.Text}";
}

/// <summary>
/// Bir revizyonun değişen dosyalarını ve seçili dosyanın diff'ini gösterir (P04-T08, P04-T09).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bilinçli olarak ana pencereden BAĞIMSIZ.</b> Aynı bileşen iki yerde kullanılacak:
/// ana penceredeki panel ve <c>P04-T16</c>'daki karşılaştırma penceresi. Karar
/// GitExtensions'a bakılarak verildi — orada da gömülü bir diff alanı (<c>FormBrowse</c>)
/// ve ayrıca <b>modeless</b> açılan bir karşılaştırma penceresi (<c>FormDiff</c>, <c>Show()</c>
/// ile, aynı anda birden fazla) var.
/// </para>
/// <para>
/// Bu yüzden burası ne <c>MainWindow</c>'u ne de commit listesini tanır; dışarıdan
/// <b>ne gösterileceği</b> söylenir.
/// </para>
/// </remarks>
public sealed partial class DiffViewModel : ViewModelBase
{
    /// <summary>
    /// Okuma başlamadan önce beklenen süre.
    /// </summary>
    /// <remarks>
    /// Kullanıcı commit listesinde <c>↓</c> tuşuna basılı tutabilir; her satır için bir
    /// <c>git</c> süreci başlatmamak adına bekleniyor ve seçim değişince iptal ediliyor.
    /// P03-T15'teki imza okumasında aynı çözüm uygulanmıştı.
    /// </remarks>
    private static readonly TimeSpan _loadDelay = TimeSpan.FromMilliseconds(150);

    private readonly IDiffReader _reader;

    private CancellationTokenSource? _loading;
    private IReadOnlyList<FileChangeRow> _allFiles = [];

    public DiffViewModel(IDiffReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>Filtreden geçen dosyalar.</summary>
    public ObservableCollection<FileChangeRow> Files { get; } = [];

    /// <summary>Filtreden bağımsız, klasörlere göre gruplanmış ağaç.</summary>
    public ObservableCollection<FileTreeNode> Tree { get; } = [];

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    public FileChangeRow? SelectedFile =>
        SelectedIndex >= 0 && SelectedIndex < Files.Count ? Files[SelectedIndex] : null;

    partial void OnSelectedIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedFile));
        ShowSelectedFileLines();
    }

    /// <summary>Seçili dosyanın diff satırları (P04-T09).</summary>
    public AvaloniaList<DiffLineRow> Lines { get; } = [];

    /// <summary>Seçili dosyanın yan yana satırları (P04-T10).</summary>
    public AvaloniaList<SideBySideLineRow> SideLines { get; } = [];

    /// <summary>
    /// Yan yana görünüm mü, birleşik (unified) görünüm mü?
    /// </summary>
    /// <remarks>
    /// İki liste de <b>aynı</b> <see cref="FileDiff"/>'ten üretiliyor ve mod değişince
    /// yalnızca gösterilen liste değişiyor; yeniden <c>git</c> çalıştırılmıyor.
    /// </remarks>
    [ObservableProperty]
    public partial bool ShowSideBySide { get; set; }

    partial void OnShowSideBySideChanged(bool value)
    {
        ShowSelectedFileLines();

        OnPropertyChanged(nameof(ShowUnifiedLines));
        OnPropertyChanged(nameof(ShowSideLines));

        // Mod değişince duraklanan satır sıfırlanıyor; komutların kapsamı da değişti.
        OnPropertyChanged(nameof(CanStageSelection));
        OnPropertyChanged(nameof(CanUnstageSelection));
        OnPropertyChanged(nameof(CanDiscardSelection));
    }

    /// <summary>Seçili dosyada gösterilecek içerik var mı?</summary>
    [ObservableProperty]
    public partial bool HasLines { get; private set; }

    /// <summary>Birleşik liste görünür mü?</summary>
    /// <remarks>
    /// İki koşul (<see cref="HasLines"/> ve mod) arayüzde değil <b>burada</b> birleştiriliyor:
    /// Avalonia'da bileşik koşullu <c>IsVisible</c> bağlaması sessizce yanlış davranabiliyor
    /// (Faz 03'te ölçüldü — öğe hiç görünmüyordu ve hiçbir test şikâyet etmemişti).
    /// </remarks>
    public bool ShowUnifiedLines => HasLines && !ShowSideBySide;

    /// <summary>Yan yana liste görünür mü?</summary>
    public bool ShowSideLines => HasLines && ShowSideBySide;

    partial void OnHasLinesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUnifiedLines));
        OnPropertyChanged(nameof(ShowSideLines));
    }

    /// <summary>İçerik gösterilemiyorsa sebebi (binary, çok büyük, yalnızca mod değişikliği…).</summary>
    [ObservableProperty]
    public partial string? ContentNotice { get; private set; }

    /// <summary>
    /// Seçili dosyanın satırlarını listeye aktarır.
    /// </summary>
    /// <remarks>
    /// Hunk başlıkları ve içerik satırları <b>tek düz listede</b> akıyor: sanallaştırma
    /// böyle çalışıyor. Gruplanmış bir yapı, satır başına kontrol yaratmak demek olurdu
    /// (Faz 03'te aynı gerekçeyle commit listesi de düz tutulmuştu).
    /// </remarks>
    private void ShowSelectedFileLines()
    {
        Lines.Clear();
        SideLines.Clear();
        ContentNotice = null;

        // İki listenin indeksleri farklı; dosya veya mod değişince duraklanan satır anlamını
        // yitirir (P04-T12).
        CurrentLineIndex = -1;
        LineSearchStatus = null;

        FileDiff? diff = SelectedFile?.Diff;

        if (diff is null)
        {
            HasLines = false;
            return;
        }

        if (!diff.HasHunks)
        {
            // Hunk'sız diff türleri normaldir (P04-T02'de ölçüldü); kullanıcıya NEDEN
            // içerik olmadığı söylenmeli, boş bir alan bırakılmamalı.
            ContentNotice = diff switch
            {
                { IsTooLarge: true } => Loc.T("diff.this_file_is_too_large_its_content_was_not_l"),
                { IsBinary: true } => Loc.T("diff.binary_file_the_content_cannot_be_shown"),
                { IsModeOnlyChange: true } => $"Only the file mode changed: {diff.OldMode} → {diff.NewMode}",
                { Change: FileChangeKind.Renamed } => Loc.T("diff.the_file_was_moved_the_content_did_not_chang"),
                _ => Loc.T("diff.no_changes_to_show"),
            };

            HasLines = false;
            return;
        }

        if (ShowSideBySide)
        {
            // Hizalama çekirdek katmanda (`SideBySideDiff`) ve satır içi vurgulamayla
            // AYNI eşlemeyi kullanıyor — ikisi ayrışsaydı kullanıcı aynı ekranda çelişkili
            // iki cevap görürdü.
            DiffTextOptions text = TextOptions;

            SideLines.AddRange(SideBySideDiff.Build(diff).Select(row =>
                row.IsHunkHeader
                    ? SideBySideLineRow.ForHunk(row.HunkHeader!, row.HunkIndex)
                    : SideBySideLineRow.ForRow(row, text)));
        }
        else
        {
            DiffTextOptions text = TextOptions;
            List<DiffLineRow> rows = [];

            for (int hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
            {
                DiffHunk hunk = diff.Hunks[hunkIndex];

                rows.Add(DiffLineRow.ForHunk(hunk, hunkIndex));

                for (int lineIndex = 0; lineIndex < hunk.Lines.Count; lineIndex++)
                {
                    rows.Add(DiffLineRow.ForLine(hunk.Lines[lineIndex], text, hunkIndex, lineIndex));
                }
            }

            Lines.AddRange(rows);
        }

        HasLines = true;
    }

    // ---- P04-T13: görsel ayarlar ----

    /// <summary>
    /// Boşluk ve sekmeler görünür karakterlerle gösterilsin mi?
    /// </summary>
    /// <remarks>
    /// GitExtensions'ta da <b>tek anahtar</b> ("Show non-printing characters"): boşluk ve
    /// sekme ayrı ayrı değil birlikte açılıyor.
    /// </remarks>
    [ObservableProperty]
    public partial bool ShowWhitespace { get; set; }

    /// <summary>
    /// Bir sekmenin kaç sütun ilerlettiği.
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> Avalonia sekmeyi tab-stop olarak değil <b>sabit dört boşluk</b>
    /// genişliğinde çiziyor ve ayarlanamıyor; dönüşüm bu yüzden
    /// <see cref="DiffTextFormatter"/>'da yapılıyor.
    /// </remarks>
    [ObservableProperty]
    public partial int TabWidth { get; set; } = 4;

    /// <summary>
    /// Uzun satırlar kaydırılsın (sarılsın) mı?
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> değişken satır yüksekliği sanallaştırmayı bozmuyor (500 öğede 8
    /// konteyner gerçekleşiyor) ve yan yana görünümde iki taraf <c>Grid</c> satırı sayesinde
    /// <b>hizada kalıyor</b> — sarılan taraf diğerini de yükseltiyor.
    /// </remarks>
    [ObservableProperty]
    public partial bool WordWrap { get; set; }

    /// <summary>Diff metninin punto büyüklüğü.</summary>
    /// <remarks>
    /// Punto Avalonia'da <b>kalıtsal</b> olduğu için kök öğede bir kez veriliyor; satır
    /// şablonlarına tek tek bağlanmıyor.
    /// </remarks>
    [ObservableProperty]
    public partial double FontSize { get; set; } = 12;

    partial void OnShowWhitespaceChanged(bool value) => ShowSelectedFileLines();

    partial void OnTabWidthChanged(int value) => ShowSelectedFileLines();

    /// <summary>Satır üretiminde kullanılan gösterim ayarları.</summary>
    private DiffTextOptions TextOptions => new()
    {
        TabWidth = Math.Clamp(TabWidth, 1, 16),
        ShowWhitespace = ShowWhitespace,
    };

    /// <summary>Ağaç görünümü mü, düz liste mi?</summary>
    [ObservableProperty]
    public partial bool ShowAsTree { get; set; }

    partial void OnShowAsTreeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFlatFileList));
        OnPropertyChanged(nameof(ShowTreeFileList));
    }

    /// <summary>Yola göre filtre; boşsa tümü.</summary>
    [ObservableProperty]
    public partial string? Filter { get; set; }

    partial void OnFilterChanged(string? value) => ApplyFilter();

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Okuma başarısızsa gösterilecek mesaj.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Gösterilen şeyin kısa açıklaması (başlık için).</summary>
    [ObservableProperty]
    public partial string? Subject { get; private set; }

    /// <summary>Değişiklik var mı?</summary>
    public bool HasFiles => Files.Count > 0;

    /// <summary>Bir revizyon seçilmiş ama hiç dosya değişmemiş mi?</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    /// <summary>
    /// Bir commit'in değişikliklerini gösterir.
    /// </summary>
    /// <remarks>
    /// Önceki okuma iptal edilir: kullanıcı listede hızlıca gezinirken her satır için
    /// <c>git</c> çalıştırmak gereksiz.
    /// </remarks>
    public Task ShowCommitAsync(
        string? workingDirectory,
        CommitId commit,
        string? subject = null,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // ⚠️ Bu bölüm SENKRON olmak zorunda. İptal ile yeni jetonun atanması arasına bir
        // `await` girerse art arda gelen çağrılar birbirini iptal edemez: hepsi henüz
        // atanmamış `_loading`'i görüp geçer ve HER BİRİ git çalıştırır. Bir test bunu
        // yakaladı — 21 hızlı seçim 21 okuma üretiyordu.
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;

        if (string.IsNullOrEmpty(workingDirectory) || commit.IsEmpty)
        {
            Clear();
            return Task.CompletedTask;
        }

        Subject = subject;

        _loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        return LoadAsync(workingDirectory, commit, options, _loading.Token);
    }

    /// <summary>
    /// İki revizyon arasındaki farkı gösterir (P04-T16).
    /// </summary>
    /// <remarks>
    /// <paramref name="toRevision"/> <see langword="null"/> ise <b>çalışma dizini</b>
    /// karşılaştırılır (<c>git diff &lt;rev&gt;</c>).
    /// </remarks>
    public Task ShowRangeAsync(
        string? workingDirectory,
        string fromRevision,
        string? toRevision,
        string? subject = null,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromRevision);

        // ShowCommitAsync ile aynı gerekçe: iptal ile yeni jetonun atanması arasına `await`
        // giremez, yoksa art arda gelen çağrılar birbirini iptal edemez.
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;

        if (string.IsNullOrEmpty(workingDirectory))
        {
            Clear();
            return Task.CompletedTask;
        }

        Subject = subject;

        _loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        return LoadRangeAsync(workingDirectory, fromRevision, toRevision, options, _loading.Token);
    }

    /// <summary>
    /// Çalışma ağacındaki değişiklikleri gösterir (P05-T09).
    /// </summary>
    /// <param name="workingDirectory">Depo çalışma dizini.</param>
    /// <param name="staged">
    /// <see langword="true"/> ise index ↔ <c>HEAD</c> (stage'lenmiş), aksi halde çalışma ağacı
    /// ↔ index (stage'lenmemiş).
    /// </param>
    /// <param name="subject">Panel başlığı.</param>
    /// <param name="options">Diff seçenekleri.</param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    /// <remarks>
    /// İki ayrı okuma, çünkü aynı dosyanın <b>iki farklı</b> diff'i olabiliyor: stage'lenmiş
    /// hâli ile stage'lenmemiş hâli. Kullanıcı hangi listedeki satıra bastıysa onu görmeli.
    /// </remarks>
    public Task ShowWorkingTreeAsync(
        string? workingDirectory,
        bool staged,
        string? subject = null,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // ShowCommitAsync ile aynı gerekçe: iptal ile yeni jetonun atanması arasına `await`
        // giremez, yoksa art arda gelen çağrılar birbirini iptal edemez.
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;

        if (string.IsNullOrEmpty(workingDirectory))
        {
            Clear();
            return Task.CompletedTask;
        }

        Subject = subject;

        _loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        CancellationToken token = _loading.Token;

        return LoadCoreAsync(
            (reader, effective, inner) => staged
                ? reader.ReadStagedAsync(workingDirectory, effective, inner)
                : reader.ReadUnstagedAsync(workingDirectory, effective, inner),
            options,
            token);
    }

    private Task LoadRangeAsync(
        string workingDirectory,
        string fromRevision,
        string? toRevision,
        DiffOptions? options,
        CancellationToken token) =>
        LoadCoreAsync(
            (reader, effective, inner) => toRevision is null
                ? reader.ReadAgainstWorkingTreeAsync(workingDirectory, fromRevision, effective, inner)
                : reader.ReadBetweenAsync(workingDirectory, fromRevision, toRevision, effective, inner),
            options,
            token);

    private Task LoadAsync(
        string workingDirectory,
        CommitId commit,
        DiffOptions? options,
        CancellationToken token) =>
        LoadCoreAsync(
            (reader, effective, inner) =>
                reader.ReadCommitAsync(workingDirectory, commit, effective, inner),
            options,
            token);

    /// <summary>
    /// Okuma iskeleti: gecikme, iptal, hata ve yükleme durumu tek yerde.
    /// </summary>
    /// <remarks>
    /// Üç giriş noktası (commit · aralık · çalışma ağacı) yalnızca <b>hangi okumanın</b>
    /// yapılacağında ayrışıyor; gerisi aynı. Ayrı ayrı yazmak, iptal mantığının üç kopyası
    /// demekti — o mantık P04-T08'de bir kez zaten sessizce yanlış çalışmıştı.
    /// </remarks>
    private async Task LoadCoreAsync(
        Func<IDiffReader, DiffOptions, CancellationToken, Task<IReadOnlyList<FileDiff>>> read,
        DiffOptions? options,
        CancellationToken token)
    {
        try
        {
            await Task.Delay(_loadDelay, token).ConfigureAwait(true);

            IsLoading = true;
            ErrorMessage = null;

            // Satır içi fark varsayılan olarak açık: değişen satırda TAM OLARAK neyin
            // değiştiğini görmek diff okumanın asıl faydası. Yerel hesaplandığı için
            // ek `git` süreci yok (P04-T05).
            IReadOnlyList<FileDiff> diffs = await read(
                    _reader,
                    options ?? new DiffOptions
                    {
                        WordLevel = true,

                        // Yazma yolu da bu kodlamayı kullanıyor; ikisi ayrışırsa üretilen
                        // yama dosyanın baytlarına uymaz ve git reddeder (P05-T16).
                        ContentEncoding = ContentEncoding,
                    },
                    token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            Apply(diffs);
        }
        catch (OperationCanceledException)
        {
            // Kullanıcı başka bir şey seçti; hata değil.
        }
        catch (Exception ex) when (ex is GitException or DiffParseException)
        {
            Clear();

            // GitException'da mesaj Kind'a göre çevriliyor (P11-T06); ayrıştırma hatası
            // sınıflandırılmıyor, kendi mesajı gösteriliyor.
            ErrorMessage = ex is GitException classified ? Loc.GitError(classified) : ex.Message;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    // ---- P04-T12: gezinme, arama, kopyalama ----

    /// <summary>
    /// Diff içinde duraklanan satırın indeksi; yoksa <c>-1</c>.
    /// </summary>
    /// <remarks>
    /// Aktif listenin (unified veya yan yana) indeksidir. Dosya ya da mod değişince
    /// <b>sıfırlanır</b>: iki listenin indeksleri farklı, eski değer başka bir satırı
    /// gösterirdi.
    /// </remarks>
    [ObservableProperty]
    public partial int CurrentLineIndex { get; private set; } = -1;

    /// <summary>Diff içinde aranan metin.</summary>
    [ObservableProperty]
    public partial string? LineSearchText { get; set; }

    /// <summary>Arama sonucu bulunamadıysa kullanıcıya gösterilecek not.</summary>
    /// <remarks>
    /// Bulunamayan aramada sessiz kalmak, kullanıcının kısayolun çalışmadığını sanmasına
    /// yol açar (Faz 03'te commit listesinde aynı karar verilmişti).
    /// </remarks>
    [ObservableProperty]
    public partial string? LineSearchStatus { get; private set; }

    /// <summary>Aktif listedeki satır sayısı — mod'a göre unified ya da yan yana.</summary>
    private int ActiveLineCount => ShowSideBySide ? SideLines.Count : Lines.Count;

    /// <summary>
    /// <paramref name="index"/> satırı bir <b>değişiklik</b> satırı mı?
    /// </summary>
    /// <remarks>
    /// Yan yana modda satırın <b>iki tarafı</b> da bakılır: sol silinmiş ya da sağ eklenmişse
    /// o satır bir değişikliktir.
    /// </remarks>
    private bool IsChangeLine(int index)
    {
        if (ShowSideBySide)
        {
            SideBySideLineRow row = SideLines[index];

            return !row.IsHunkHeader
                && (IsChangeKind(row.Left) || IsChangeKind(row.Right));
        }

        DiffLineRow line = Lines[index];

        return !line.IsHunkHeader && IsChangeKind(line);
    }

    private static bool IsChangeKind(DiffLineRow row) =>
        !row.IsFiller && row.Kind is DiffLineKind.Added or DiffLineKind.Removed;

    private bool IsHunkHeaderLine(int index) =>
        ShowSideBySide ? SideLines[index].IsHunkHeader : Lines[index].IsHunkHeader;

    /// <summary>
    /// Sonraki <b>değişiklik bloğunun</b> başına gider.
    /// </summary>
    /// <remarks>
    /// "Sonraki hunk" değil <b>"sonraki değişiklik"</b>: GitExtensions'ın
    /// <c>GoToNextChange</c>'i de böyle. Büyük bir hunk'ın içinde başlığa atlamak aynı yerde
    /// saymak olurdu. <b>Ardışık</b> değişiklik satırları tek blok sayılır — silinen ve onun
    /// yerine eklenen satır kullanıcı için tek bir değişikliktir.
    /// </remarks>
    public bool GoToNextChange() => GoToChange(forward: true);

    /// <summary>Önceki değişiklik bloğunun başına gider.</summary>
    public bool GoToPreviousChange() => GoToChange(forward: false);

    private bool GoToChange(bool forward)
    {
        int count = ActiveLineCount;

        if (count == 0)
        {
            return false;
        }

        int start = CurrentLineIndex;

        if (forward)
        {
            // İçinde bulunduğumuz bloğun sonuna kadar ilerle, sonra sıradaki bloğun başını ara.
            int index = start < 0 ? 0 : start + 1;

            while (index < count && start >= 0 && IsChangeLine(index))
            {
                index++;
            }

            for (; index < count; index++)
            {
                if (IsChangeLine(index))
                {
                    CurrentLineIndex = index;
                    return true;
                }
            }

            return false;
        }

        int back = start < 0 ? count - 1 : start - 1;

        // Geriye giderken bloğun BAŞINA konulmalı; ortasına düşmek "önceki değişiklik"
        // tuşuna bir kez daha basınca aynı blokta kalmak demek olurdu.
        while (back >= 0 && !IsChangeLine(back))
        {
            back--;
        }

        if (back < 0)
        {
            return false;
        }

        while (back > 0 && IsChangeLine(back - 1))
        {
            back--;
        }

        if (back == start)
        {
            return false;
        }

        CurrentLineIndex = back;
        return true;
    }

    /// <summary>Sonraki hunk başlığına gider.</summary>
    public bool GoToNextHunk() => GoToHunk(forward: true);

    /// <summary>Önceki hunk başlığına gider.</summary>
    public bool GoToPreviousHunk() => GoToHunk(forward: false);

    private bool GoToHunk(bool forward)
    {
        int count = ActiveLineCount;

        if (count == 0)
        {
            return false;
        }

        int step = forward ? 1 : -1;
        int index = CurrentLineIndex < 0
            ? (forward ? 0 : count - 1)
            : CurrentLineIndex + step;

        for (; index >= 0 && index < count; index += step)
        {
            if (IsHunkHeaderLine(index))
            {
                CurrentLineIndex = index;
                return true;
            }
        }

        return false;
    }

    /// <summary>Listedeki sonraki dosyaya geçer.</summary>
    public bool GoToNextFile() => GoToFile(1);

    /// <summary>Listedeki önceki dosyaya geçer.</summary>
    public bool GoToPreviousFile() => GoToFile(-1);

    private bool GoToFile(int delta)
    {
        int target = SelectedIndex + delta;

        if (target < 0 || target >= Files.Count)
        {
            return false;
        }

        SelectedIndex = target;
        return true;
    }

    /// <summary>
    /// Arananın sonraki eşleşmesine gider; sona gelince <b>başa sarar</b>.
    /// </summary>
    /// <remarks>
    /// Başa sarmak şart: aranan şey imlecin üstünde kaldıysa "bulunamadı" demek yanıltıcı
    /// olurdu. Arama <b>ham metinde</b> yapılıyor — boşluk gösterimi açıkken metin
    /// <c>·</c>/<c>»</c> içeriyor ve kullanıcı sekmeyi göstergesiyle aramaz (P04-T13).
    /// </remarks>
    public bool FindNext() => Find(forward: true);

    /// <summary>Arananın önceki eşleşmesine gider; başa gelince sona sarar.</summary>
    public bool FindPrevious() => Find(forward: false);

    private bool Find(bool forward)
    {
        string? needle = LineSearchText?.Trim();
        int count = ActiveLineCount;

        if (string.IsNullOrEmpty(needle) || count == 0)
        {
            LineSearchStatus = null;
            return false;
        }

        int step = forward ? 1 : -1;

        for (int offset = 1; offset <= count; offset++)
        {
            // `+ count` : negatif yönde de modülo doğru çalışsın.
            int index = (((CurrentLineIndex + (step * offset)) % count) + count) % count;

            if (LineMatches(index, needle))
            {
                CurrentLineIndex = index;
                LineSearchStatus = null;
                return true;
            }
        }

        LineSearchStatus = Loc.T("diff.not_found");
        return false;
    }

    private bool LineMatches(int index, string needle)
    {
        if (!ShowSideBySide)
        {
            return Contains(Lines[index].RawText, needle);
        }

        SideBySideLineRow row = SideLines[index];

        return Contains(row.Left.RawText, needle) || Contains(row.Right.RawText, needle);
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Görünen diff'i panoya konacak metne çevirir.
    /// </summary>
    /// <param name="mode">Kopyalama biçimi; varsayılan öneksiz kod.</param>
    /// <param name="selection">
    /// Kopyalanacak satır indeksleri; <see langword="null"/> ya da boşsa <b>tamamı</b>.
    /// </param>
    /// <remarks>
    /// ⚠️ Metin <see cref="DiffLineRow.RawText"/> üzerinden üretiliyor: gösterim dönüşümü
    /// uygulanmış metni kopyalamak panoya <b>bozuk kod</b> koyardı (sekmeler açılmış,
    /// boşluklar <c>·</c> olmuş).
    /// </remarks>
    public string CopyText(DiffCopyMode mode = DiffCopyMode.Code, IReadOnlyList<int>? selection = null)
    {
        int count = ActiveLineCount;

        if (count == 0)
        {
            return string.Empty;
        }

        HashSet<int>? wanted = selection is { Count: > 0 } ? [.. selection] : null;
        StringBuilder builder = new();

        for (int index = 0; index < count; index++)
        {
            if (wanted is not null && !wanted.Contains(index))
            {
                continue;
            }

            AppendCopyLine(builder, index, mode);
        }

        return builder.ToString().TrimEnd('\n');
    }

    private void AppendCopyLine(StringBuilder builder, int index, DiffCopyMode mode)
    {
        if (ShowSideBySide)
        {
            SideBySideLineRow row = SideLines[index];

            if (row.IsHunkHeader)
            {
                if (mode == DiffCopyMode.Patch)
                {
                    builder.Append(row.Header).Append('\n');
                }

                return;
            }

            // ⚠️ Bağlam satırı yan yana modda İKİ tarafta da duruyor; yamaya iki kez
            // yazılırsa üretilen yama geçersiz olur.
            if (IsChangeKind(row.Left))
            {
                Append(builder, row.Left, mode);
            }

            if (IsChangeKind(row.Right))
            {
                Append(builder, row.Right, mode);
            }

            if (!IsChangeKind(row.Left) && !IsChangeKind(row.Right) && !row.Left.IsFiller)
            {
                Append(builder, row.Left, mode);
            }

            return;
        }

        DiffLineRow line = Lines[index];

        if (line.IsHunkHeader)
        {
            if (mode == DiffCopyMode.Patch)
            {
                builder.Append(line.Text).Append('\n');
            }

            return;
        }

        Append(builder, line, mode);
    }

    private static void Append(StringBuilder builder, DiffLineRow line, DiffCopyMode mode)
    {
        if (line.IsFiller)
        {
            return;
        }

        // "Eski hâl" ve "yeni hâl" karşı tarafı atlar: kullanıcı dosyanın o sürümünü
        // istiyor, diff'i değil.
        if (mode == DiffCopyMode.OldVersion && line.Kind == DiffLineKind.Added)
        {
            return;
        }

        if (mode == DiffCopyMode.NewVersion && line.Kind == DiffLineKind.Removed)
        {
            return;
        }

        if (mode == DiffCopyMode.Patch)
        {
            builder.Append(line.Kind switch
            {
                DiffLineKind.Added => '+',
                DiffLineKind.Removed => '-',
                _ => ' ',
            });
        }

        builder.Append(line.RawText).Append('\n');
    }

    /// <summary>
    /// Bileşenin kendi dosya listesi gösterilsin mi? (P05-T09)
    /// </summary>
    /// <remarks>
    /// Çalışma dizini görünümünde dosya listesi <b>solda</b>, stage'lenmiş/stage'lenmemiş
    /// olarak ikiye ayrılmış hâlde duruyor. Bileşenin kendi listesini de göstermek, aynı
    /// dosyaları iki ayrı yerde listeleyip kullanıcıya "seçim hangisinden?" sorusunu
    /// sordururdu.
    /// </remarks>
    [ObservableProperty]
    public partial bool ShowFileList { get; set; } = true;

    /// <summary>Düz dosya listesi görünsün mü?</summary>
    /// <remarks>
    /// Bileşik <c>IsVisible</c> koşulu XAML'de değil <b>burada</b>: Faz 03'te bileşik
    /// bağlamanın sessizce yanlış davrandığı ölçülmüştü (P04-T10'da da aynı karar).
    /// </remarks>
    public bool ShowFlatFileList => ShowFileList && !ShowAsTree;

    /// <summary>Ağaç görünümü görünsün mü?</summary>
    public bool ShowTreeFileList => ShowFileList && ShowAsTree;

    partial void OnShowFileListChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFlatFileList));
        OnPropertyChanged(nameof(ShowTreeFileList));
    }

    /// <summary>
    /// Yolu verilen dosyayı seçer; listede yoksa seçim değişmez.
    /// </summary>
    /// <remarks>
    /// Dışarıdaki bir liste seçim kaynağı olduğunda kullanılır (P05-T09). Yol bulunamazsa
    /// <see langword="false"/> döner — sessizce başka bir dosyayı göstermek, kullanıcıya
    /// seçmediği bir içeriği doğruymuş gibi sunmak olurdu.
    /// </remarks>
    public bool SelectPath(RepositoryPath path)
    {
        for (int index = 0; index < Files.Count; index++)
        {
            if (Files[index].Path == path)
            {
                SelectedIndex = index;
                return true;
            }
        }

        return false;
    }

    // ---- P05-T10: kısmi staging ----

    /// <summary>
    /// Kısmi staging'i gerçekleştirecek taraf; yoksa eylemler kapalı.
    /// </summary>
    /// <remarks>
    /// Bileşen <b>bağımsız</b> kalıyor (P04-T08 kararı): commit geçmişinde ve karşılaştırma
    /// penceresinde staging anlamsız, orada bu alan <see langword="null"/> ve menü öğeleri
    /// devre dışı görünüyor.
    /// </remarks>
    public IPartialStagingHost? StagingHost
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(CanStageSelection));
            OnPropertyChanged(nameof(CanUnstageSelection));
        }
    }

    /// <summary>
    /// "Seçili satırları stage'le" kullanılabilir mi?
    /// </summary>
    /// <remarks>
    /// GitExtensions'ta da iki komut <b>birbirini dışlıyor</b>:
    /// <c>stageSelectedLines</c> yalnızca çalışma ağacı tarafında, <c>unstageSelectedLines</c>
    /// yalnızca index tarafında görünüyor (<c>FileViewer.cs</c>). Aynı anda ikisini birden
    /// sunmak, kullanıcıya bulunduğu tarafta anlamsız bir eylem göstermek olurdu.
    /// <para>
    /// Yan yana modda da geçerli (P05-T11): satırlar hunk ve satır indeksini taşıdığı için
    /// seçim orada da kesin olarak çevrilebiliyor.
    /// </para>
    /// </remarks>
    public bool CanStageSelection => StagingHost?.CanStage == true;

    /// <summary>"Seçili satırları geri al" kullanılabilir mi?</summary>
    public bool CanUnstageSelection => StagingHost?.CanUnstage == true;

    /// <summary>
    /// Diff içeriğinin okunacağı ve yamanın yazılacağı kodlama (P05-T16).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Okuma ve yazma AYNI kodlamayı kullanmak zorunda.</b> P05-T16'da gerçek bir
    /// depoda ölçüldü: Latin-5 içerikli bir dosyada diff UTF-8 varsayılanıyla okunup yama
    /// UTF-8 ile yazılınca <c>git apply</c> yamayı <b>reddediyor</b>
    /// (<c>patch does not apply</c>) — çünkü UTF-8 çözümü ham baytları geri getirmiyor.
    /// Kodlama uçtan uca geçirildiğinde işlem başarılı ve index'e doğru baytlar giriyor.
    /// </para>
    /// <para>
    /// Yanlış içerik <b>sessizce</b> yazılmıyor: git yamayı reddediyor. Bu, P05-T04'teki
    /// "<c>--recount</c> kullanma" kararının karşılığı — git'in doğrulaması açık bırakıldığı
    /// için hata görünür oluyor.
    /// </para>
    /// <para>
    /// Varsayılan (<see langword="null"/>) UTF-8. Kullanıcıya kodlama seçtirmek ayarlar
    /// altyapısına bağlı (<b>P08-T14</b>); burada altyapı hazır.
    /// </para>
    /// </remarks>
    public System.Text.Encoding? ContentEncoding { get; set; }

    /// <summary>
    /// Seçili satırlar çalışma ağacından atılabilir mi (P05-T15)?
    /// </summary>
    /// <remarks>
    /// Yalnızca çalışma ağacı tarafında anlamlı: index tarafında "sıfırla" zaten
    /// <i>unstage</i> demek olurdu ve iki komut aynı şeyi yapardı.
    /// </remarks>
    public bool CanDiscardSelection => StagingHost?.CanStage == true;

    /// <summary>
    /// Seçili satırlardan bir yama seçimi kurar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hunk başlığı seçiliyse o hunk'ın tamamı seçilir.</b> Ayrı bir "bu hunk'ı stage'le"
    /// komutu yok — GitExtensions'ta da yok; orada da tek komut var ve kapsamı seçime
    /// bakıyor. Başlık satırı zaten "bu hunk" demenin doğal yolu.
    /// </para>
    /// <para>
    /// Hiç satır seçili değilse <b>duraklanan satır</b> kullanılır; o da yoksa seçim boş
    /// döner ve çağıran hiçbir şey yapmaz — "hiçbir şey seçmeden stage'le" sessizce tüm
    /// dosyayı stage'lemek olurdu.
    /// </para>
    /// </remarks>
    public PatchSelection? BuildSelection(IReadOnlyList<int>? selectedRowIndices)
    {
        if (SelectedFile?.Diff is not { } diff)
        {
            return null;
        }

        IReadOnlyList<int> indices = selectedRowIndices is { Count: > 0 }
            ? selectedRowIndices
            : CurrentLineIndex >= 0 ? [CurrentLineIndex] : [];

        if (indices.Count == 0)
        {
            return null;
        }

        HashSet<(int Hunk, int Line)> lines = [];

        // Sınır AKTİF listeye göre: yan yana modda `Lines` boş, birleşik modda `SideLines`.
        int count = ShowSideBySide ? SideLines.Count : Lines.Count;

        foreach (int index in indices)
        {
            if (index < 0 || index >= count)
            {
                continue;
            }

            if (ShowSideBySide)
            {
                SideBySideLineRow side = SideLines[index];

                if (side.IsHunkHeader)
                {
                    AddWholeHunk(diff, side.HunkIndex, lines);
                    continue;
                }

                // ⚠️ Bir yan yana satır İKİ farklı unified satırı taşıyabiliyor (solda
                // silinen, sağda onun yerine eklenen). İkisi de seçime girmeli — yalnızca
                // birini almak, kullanıcının gördüğü çiftin yarısını stage'lemek olurdu.
                Add(side.Left, lines);
                Add(side.Right, lines);
                continue;
            }

            DiffLineRow row = Lines[index];

            if (row.IsHunkHeader)
            {
                AddWholeHunk(diff, row.HunkIndex, lines);
                continue;
            }

            Add(row, lines);
        }

        return lines.Count == 0 ? null : PatchSelection.Lines(lines);
    }

    /// <summary>
    /// Satır bir değişiklik satırıysa seçime ekler.
    /// </summary>
    /// <remarks>
    /// Bağlam satırları yamaya kendiliğinden giriyor; "seçildi" saymak, kullanıcının
    /// seçmediği bir değişikliği de almak olurdu. Dolgu satırının karşılığı hiç yok.
    /// </remarks>
    private static void Add(DiffLineRow row, HashSet<(int Hunk, int Line)> lines)
    {
        if (row.IsHunkHeader || row.IsFiller)
        {
            return;
        }

        if (row.Kind is DiffLineKind.Added or DiffLineKind.Removed
            && row.HunkIndex >= 0
            && row.LineIndex >= 0)
        {
            lines.Add((row.HunkIndex, row.LineIndex));
        }
    }

    private static void AddWholeHunk(
        FileDiff diff,
        int hunkIndex,
        HashSet<(int Hunk, int Line)> lines)
    {
        if (hunkIndex < 0 || hunkIndex >= diff.Hunks.Count)
        {
            return;
        }

        DiffHunk hunk = diff.Hunks[hunkIndex];

        for (int line = 0; line < hunk.Lines.Count; line++)
        {
            if (hunk.Lines[line].Kind != DiffLineKind.Context)
            {
                lines.Add((hunkIndex, line));
            }
        }
    }

    /// <summary>
    /// Kısmi staging komutlarının kullanılabilirliğinin değiştiğini bildirir.
    /// </summary>
    /// <remarks>
    /// Kullanılabilirlik <see cref="StagingHost"/>'un durumundan geliyor ve o durum dışarıda
    /// değişiyor (kullanıcı öbür listeye geçti); bileşen bunu kendiliğinden göremez.
    /// </remarks>
    public void NotifyStagingAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanStageSelection));
        OnPropertyChanged(nameof(CanUnstageSelection));
        OnPropertyChanged(nameof(CanDiscardSelection));
    }

    /// <summary>Seçili satırları stage'ler.</summary>
    public Task StageSelectionAsync(IReadOnlyList<int>? selectedRowIndices = null) =>
        ApplySelectionAsync(selectedRowIndices, stage: true);

    /// <summary>Seçili satırları index'ten geri alır.</summary>
    public Task UnstageSelectionAsync(IReadOnlyList<int>? selectedRowIndices = null) =>
        ApplySelectionAsync(selectedRowIndices, stage: false);

    /// <summary>
    /// Seçili satırlardaki değişiklikleri çalışma ağacından atar (P05-T15) — <b>yıkıcı</b>.
    /// </summary>
    public async Task DiscardSelectionAsync(IReadOnlyList<int>? selectedRowIndices = null)
    {
        if (StagingHost is not { } host
            || SelectedFile?.Diff is not { } diff
            || BuildSelection(selectedRowIndices) is not { } selection)
        {
            return;
        }

        try
        {
            ErrorMessage = null;

            await host.DiscardAsync(diff, selection).ConfigureAwait(true);
        }
        catch (GitException ex)
        {
            ErrorMessage = Loc.GitError(ex);
        }
    }

    private async Task ApplySelectionAsync(IReadOnlyList<int>? selectedRowIndices, bool stage)
    {
        if (StagingHost is not { } host
            || SelectedFile?.Diff is not { } diff
            || BuildSelection(selectedRowIndices) is not { } selection)
        {
            return;
        }

        try
        {
            ErrorMessage = null;

            await host.ApplyAsync(diff, selection, stage).ConfigureAwait(true);
        }
        catch (GitException ex)
        {
            // `git apply` sayı/bağlam hatalarını REDDEDİYOR (P05-T04'te ölçüldü); mesaj
            // kullanıcıya ulaşmalı, yoksa "tıkladım ama bir şey olmadı" durumu oluşur.
            ErrorMessage = Loc.GitError(ex);
        }
    }

    /// <summary>Görünümü boşaltır — depo kapanınca dışarıdan da çağrılıyor.</summary>
    public void Clear()
    {
        _allFiles = [];
        Files.Clear();
        Tree.Clear();
        Lines.Clear();
        SideLines.Clear();

        SelectedIndex = -1;
        CurrentLineIndex = -1;
        LineSearchStatus = null;
        ContentNotice = null;
        Subject = null;
        HasLines = false;
        IsEmpty = false;

        OnPropertyChanged(nameof(HasFiles));
    }

    private void Apply(IReadOnlyList<FileDiff> diffs)
    {
        _allFiles = [.. diffs.Select(d => new FileChangeRow(d))];

        ApplyFilter();

        // Değişiklik yoksa bu bir hata değil: boş commit veya yalnızca yoksayılan farklar
        // (P04-T04'te ölçüldü: `--ignore-blank-lines` dosyayı listede bırakıyor ama
        // `-w` aynılaşan dosyayı düşürüyor).
        IsEmpty = _allFiles.Count == 0;
    }

    private void ApplyFilter()
    {
        Files.Clear();

        string? filter = Filter?.Trim();

        foreach (FileChangeRow row in _allFiles)
        {
            if (filter is { Length: > 0 }
                && !row.Path.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Files.Add(row);
        }

        RebuildTree();

        // Filtre seçili dosyayı elediyse seçim düşer; ilk satıra dönmek kullanıcıyı
        // beklemediği bir dosyaya götürmekten iyidir.
        SelectedIndex = Files.Count > 0 ? 0 : -1;

        OnPropertyChanged(nameof(HasFiles));
    }

    /// <summary>
    /// Filtreden geçen dosyalardan klasör ağacı kurar.
    /// </summary>
    /// <remarks>
    /// Ağaç <b>her filtre değişiminde</b> yeniden kuruluyor. Bir commit'in değiştirdiği dosya
    /// sayısı yüzler mertebesinde; burada artımlı güncelleme karmaşıklığı kazancından fazla.
    /// </remarks>
    private void RebuildTree()
    {
        Tree.Clear();

        Dictionary<string, FileTreeNode> folders = [];

        foreach (FileChangeRow row in Files)
        {
            FileTreeNode? parent = EnsureFolder(row.Directory, folders);

            FileTreeNode leaf = FileTreeNode.ForFile(row);

            if (parent is null)
            {
                Tree.Add(leaf);
            }
            else
            {
                parent.Children.Add(leaf);
            }
        }
    }

    /// <summary>Klasör düğümünü (ve gerekiyorsa üstlerini) oluşturur.</summary>
    private FileTreeNode? EnsureFolder(string directory, Dictionary<string, FileTreeNode> folders)
    {
        if (directory.Length == 0)
        {
            return null;
        }

        if (folders.TryGetValue(directory, out FileTreeNode? existing))
        {
            return existing;
        }

        int separator = directory.LastIndexOf('/');
        string name = separator < 0 ? directory : directory[(separator + 1)..];
        string parentPath = separator < 0 ? string.Empty : directory[..separator];

        FileTreeNode node = FileTreeNode.ForFolder(name);
        folders[directory] = node;

        FileTreeNode? parent = EnsureFolder(parentPath, folders);

        if (parent is null)
        {
            Tree.Add(node);
        }
        else
        {
            parent.Children.Add(node);
        }

        return node;
    }

}

/// <summary>
/// Ağaç görünümündeki bir düğüm: klasör veya dosya (P04-T08).
/// </summary>
public sealed class FileTreeNode
{
    private FileTreeNode(string name, FileChangeRow? file)
    {
        Name = name;
        File = file;
    }

    public static FileTreeNode ForFolder(string name) => new(name, null);

    public static FileTreeNode ForFile(FileChangeRow file) => new(file.Name, file);

    public string Name { get; }

    /// <summary>Yaprak düğümde dosya; klasörde <see langword="null"/>.</summary>
    public FileChangeRow? File { get; }

    public bool IsFolder => File is null;

    public ObservableCollection<FileTreeNode> Children { get; } = [];

    public override string ToString() => IsFolder ? Name + "/" : Name;
}
