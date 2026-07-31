using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Boşluk farklarının nasıl ele alınacağı (P04-T04).
/// </summary>
public enum WhitespaceMode
{
    /// <summary>Boşluk farkları normal fark sayılır.</summary>
    Include,

    /// <summary>
    /// Boşluk <b>miktarındaki</b> değişiklik yoksayılır (<c>-b</c>).
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: bu mod, olmayan yere boşluk eklenmesini yoksaymıyor — öyle bir dosya
    /// diff'te kalmaya devam ediyor.
    /// </remarks>
    IgnoreChange,

    /// <summary>
    /// Tüm boşluk farkları yoksayılır (<c>-w</c>).
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: bu modda dosya <b>aynılaşırsa</b> hem ham hem yama bölümünden düşüyor
    /// (sayılar hizalı kalıyor). Aynılaşmıyorsa (örneğin boş satır eklenmişse) iki bölümde
    /// de kalıyor — <c>-w</c> satır <i>içindeki</i> boşluğu yoksayıyor, eklenen boş satırı
    /// değil.
    /// </remarks>
    IgnoreAll,
}

/// <summary>
/// Diff okuma seçenekleri (P04-T03, P04-T04).
/// </summary>
/// <remarks>
/// <b>Planda olup git'te olmayan bir madde vardı:</b> "büyük/küçük harf duyarsızlık".
/// Ölçüldü — <c>git diff --ignore-case</c> diye bir seçenek <b>yok</b> (kullanım hatası
/// veriyor). Bu yüzden karşılığı da yok.
/// </remarks>
public sealed record DiffOptions
{
    public static DiffOptions Default { get; } = new();

    /// <summary>
    /// Birleştirme commit'inde hangi ebeveyne göre karşılaştırılacak (1 tabanlı).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="null"/> ise <b>ilk ebeveyn</b> kullanılır — "bu merge ana hatta ne
    /// getirdi" görünümü. Bu varsayılan ölçümle seçildi: düz <c>git show &lt;merge&gt;</c>
    /// temiz bir merge'de <b>hiç çıktı vermiyor</b> ve kullanıcı bunu hata sanardı.
    /// (<c>--cc</c> de temiz merge'de boş; yalnızca çakışma çözümlerini gösteriyor.)
    /// </para>
    /// <para>
    /// ⚠️ <c>-m</c> bilinçli olarak <b>kullanılmıyor</b>: her ebeveyn için ayrı bir bölüm
    /// üretiyor, yani tek bir dosya listesi varsayımını bozuyor. Belirli bir ebeveyn
    /// istendiğinde <c>&lt;merge&gt;^N</c> söz dizimi kullanılıyor.
    /// </para>
    /// </remarks>
    public int? MergeParent { get; init; }

    /// <summary>
    /// Hunk çevresinde gösterilecek bağlam satırı sayısı (<c>-U</c>); <see langword="null"/>
    /// ise git'in varsayılanı (3).
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: <c>-U0</c> hunk başlığını tek satırlık biçime düşürüyor (<c>@@ -4 +4 @@</c>),
    /// yani uzunluk yazılmıyor. Ayrıştırıcı bunu 1 sayıyor.
    /// </remarks>
    public int? ContextLines { get; init; }

    /// <summary>Boşluk farklarının ele alınışı.</summary>
    public WhitespaceMode Whitespace { get; init; } = WhitespaceMode.Include;

    /// <summary>
    /// Yalnızca boş satır eklenip silinmesi yoksayılsın mı (<c>--ignore-blank-lines</c>)?
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ÖLÇÜLDÜ — bu seçenek diğerlerinden farklı davranıyor.</b> git, yalnızca boş
    /// satırı değişmiş dosyayı <b>ham bölümde bırakıyor ama yama bloğu üretmiyor</b>.
    /// Ayrıştırıcı bu yüzden sayılar uyuşmadığında blob kimliklerine göre eşleme yapıyor;
    /// eşleşmeyen dosya <b>hunk'sız</b> görünür.
    /// </remarks>
    public bool IgnoreBlankLines { get; init; }

    /// <summary>
    /// Yeniden adlandırma benzerlik eşiği (1–100); <see langword="null"/> ise git'in
    /// varsayılanı (%50).
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: %69 benzerlikli bir dosyada <c>-M50%</c> yeniden adlandırma buluyor,
    /// <c>-M90%</c> bulmuyor (ekleme + silme olarak görünüyor).
    /// </remarks>
    public int? RenameThreshold { get; init; }

    /// <summary>
    /// Kopyalanan dosyalar da tespit edilsin mi?
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ — <c>-C</c> tek başına yetmiyor.</b> Değiştirilmemiş bir dosyadan yapılan
    /// kopya <c>-C</c> ile bulunamıyor (durum <c>A</c> kalıyor); bulunması için
    /// <see cref="FindCopiesHarder"/> gerekiyor (o zaman <c>C100</c>).
    /// </remarks>
    public bool DetectCopies { get; init; }

    /// <summary>
    /// Kopya ararken değiştirilmemiş dosyalara da bakılsın mı (<c>--find-copies-harder</c>)?
    /// </summary>
    /// <remarks>
    /// git bunu <b>pahalı</b> olarak belgeliyor; varsayılan kapalı. Kullanıcı açıkça
    /// istemedikçe açılmamalı.
    /// </remarks>
    public bool FindCopiesHarder { get; init; }

    /// <summary>Kopyalama benzerlik eşiği (1–100); <see langword="null"/> ise git varsayılanı.</summary>
    public int? CopyThreshold { get; init; }

    /// <summary>
    /// Satır içi (kelime/karakter seviyesi) değişiklikler de hesaplansın mı (P04-T05)?
    /// </summary>
    /// <remarks>
    /// <b>git'in <c>--word-diff</c>'i KULLANILMIYOR</b> — ölçüldü ve satır yapısını sadık
    /// biçimde veremediği görüldü (eklenen boş satırın hangi tarafa ait olduğu çıktıda yok).
    /// Parçalar, ayrıştırıcının ürettiği kesin satır metinleri üzerinde
    /// <see cref="InlineDiff"/> ile <b>yerel olarak</b> hesaplanıyor: ek <c>git</c>
    /// çalıştırması yok, sadakat riski yok.
    /// </remarks>
    public bool WordLevel { get; init; }

    /// <summary>
    /// Bu sayıdan fazla satırı değişen dosyanın <b>içeriği okunmaz</b> (P04-T06).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0 veya negatif ise sınır yok — arayüzdeki "yine de göster" bunu kullanır.
    /// </para>
    /// <para>
    /// <b>ÖLÇÜLDÜ:</b> tamamı değişen 12,7 MB'lık bir metin dosyası <b>23 MB</b> yama
    /// üretiyor (git bunu 0,12 sn'de yapıyor — sorun git'te değil bizde). Böyle bir dosya
    /// için 800 bin <c>DiffLine</c> nesnesi yaratmak Faz 03'te ölçülen nesne başı ek yük
    /// nedeniyle uygulamayı kilitler.
    /// </para>
    /// <para>
    /// Sayılar <c>--numstat</c>'tan geliyor: <b>içerik üretilmeden</b> öğrenildiği için
    /// dosya listesinde satır sayıları yine doğru görünür.
    /// </para>
    /// <para>
    /// <b>Sınır P04-T14'te ölçümle yükseltildi (20.000 → 50.000).</b> İlk değer görüntüleyici
    /// yokken, ihtiyatla konmuştu. Gerçek ölçüm: <c>git/git</c>'te <c>po/zh_CN.po</c>'nun
    /// 43.671 satırlık diff'i <b>202 ms</b>'de satırlara dönüşüyor, 45 MB tutuyor ve
    /// kaydırmada kare süresi <b>0,7 ms</b>. Eski sınır bu ölçekteki <b>gerçek</b> dosyaları
    /// gereksiz yere "çok büyük" diye eliyordu. Asıl tehlike olan 800 bin satırlık durum
    /// hâlâ engelleniyor.
    /// </para>
    /// </remarks>
    public int MaximumChangedLines { get; init; } = 50_000;

    /// <summary>
    /// <c>git</c> çıktısı için üst sınır; aşılırsa okuma durdurulur (P04-T06).
    /// </summary>
    /// <remarks>
    /// Son savunma hattı: <see cref="MaximumChangedLines"/> dosya başına koruma sağlıyor ama
    /// binlerce orta boy dosyanın toplamı yine büyük olabilir. Sınır aşılırsa sonuç
    /// <b>ayrıştırılmaz</b>; yarım çıktıyı ayrıştırmak sessizce eksik veri üretirdi.
    /// </remarks>
    public long MaximumOutputBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// Diff içeriğinin kodlaması; <see langword="null"/> ise UTF-8 (P04-T07).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ÖLÇÜLDÜ:</b> <c>git diff</c> çıktısı <b>tek bir kodlamada değil</b> — başlıklar ve
    /// işaretler ASCII, satır içerikleri ise <b>dosyanın kendi baytları</b>. git commit
    /// mesajlarında olduğu gibi bir çeviri yapmıyor (Faz 02'de <c>i18n.logOutputEncoding</c>
    /// vardı, diff'te karşılığı <b>yok</b>). Latin-5 bir dosyada <c>0xFC</c> baytı doğrudan
    /// geliyor ve UTF-8 sanılırsa <b>sessizce bozuluyor</b>.
    /// </para>
    /// <para>
    /// Çözüm GitExtensions'ın <c>PatchProcessor</c>'ından alındı: çıktı <b>kayıpsız</b>
    /// okunuyor (her bayt bir karakter), yapı ASCII olduğu için ayrıştırma etkilenmiyor, ve
    /// satır içerikleri sonradan bu kodlamayla yeniden çözülüyor. Onlar da not düşmüş:
    /// ideali <c>.gitattributes</c>'tan dosya başına almak, şimdilik depo başına tek kodlama.
    /// </para>
    /// </remarks>
    public Encoding? ContentEncoding { get; init; }

    /// <summary>Yeniden adlandırma tespiti açık mı?</summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> tespit modern git'te <b>varsayılan olarak açık</b> (<c>diff.renames</c>).
    /// Yani <c>-M</c>'i yazmamak onu <b>kapatmıyor</b>; kapatmak için <c>--no-renames</c>
    /// gerekiyor. İki bayrak da açıkça geçiliyor, böylece davranış kullanıcının
    /// <c>.gitconfig</c>'inden bağımsız oluyor (Faz 02'de <c>i18n.logOutputEncoding</c>
    /// için verilen kararın aynısı).
    /// </remarks>
    public bool DetectRenames { get; init; } = true;
}

/// <summary>
/// Depodan diff okur (P04-T03).
/// </summary>
public interface IDiffReader
{
    /// <summary>
    /// Bir commit'in kendi getirdiği değişiklikleri okur.
    /// </summary>
    Task<IReadOnlyList<FileDiff>> ReadCommitAsync(
        string workingDirectory,
        CommitId commit,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>İki keyfi revizyon arasındaki farkı okur.</summary>
    Task<IReadOnlyList<FileDiff>> ReadBetweenAsync(
        string workingDirectory,
        string fromRevision,
        string toRevision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir revizyon ile <b>çalışma ağacı</b> arasındaki farkı okur (P04-T16).
    /// </summary>
    /// <remarks>
    /// <c>git diff &lt;rev&gt;</c>. <see cref="ReadUnstagedAsync"/>'dan farklı: o yalnızca
    /// index ile çalışma ağacını karşılaştırır, bu ise stage'lenmiş değişiklikleri de içerir.
    /// </remarks>
    Task<IReadOnlyList<FileDiff>> ReadAgainstWorkingTreeAsync(
        string workingDirectory,
        string revision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Çalışma dizini ile index arasındaki farkı okur (stage'lenmemiş değişiklikler).</summary>
    Task<IReadOnlyList<FileDiff>> ReadUnstagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Index ile <c>HEAD</c> arasındaki farkı okur (stage'lenmiş değişiklikler).</summary>
    Task<IReadOnlyList<FileDiff>> ReadStagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IDiffReader"/>
public sealed class DiffReader : IDiffReader
{
    private readonly IGitProcessRunner _runner;

    public DiffReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public Task<IReadOnlyList<FileDiff>> ReadCommitAsync(
        string workingDirectory,
        CommitId commit,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (commit.IsEmpty)
        {
            throw new ArgumentException("Commit kimliği boş olamaz.", nameof(commit));
        }

        options ??= DiffOptions.Default;

        // Belirli bir ebeveyn istendiyse `<commit>^N <commit>` ile karşılaştırılır.
        // İlk ebeveyn için bu yola gerek yok; aşağıdaki tek komut zaten onu yapıyor.
        if (options.MergeParent is > 1 and int parent)
        {
            return ReadBetweenAsync(
                workingDirectory,
                $"{commit.Value}^{parent}",
                commit.Value,
                options,
                cancellationToken);
        }

        // TEK KOMUT üç durumu birden karşılıyor (ölçüldü):
        //   --root         → kök commit'te `<sha>^` çökmesini önler
        //   --first-parent → merge'de tek ve anlamlı diff üretir (düz `git show` BOŞ döner)
        //   normal commit  → ikisi de zararsız
        List<string> arguments =
        [
            "show",
            "--root",
            "--first-parent",

            // Commit başlığı/mesajı bastırılır: ayrıştırıcı çıktının `:` ile başlamasını bekler.
            "--format=",
        ];

        AddFormatArguments(arguments, options);
        arguments.Add(commit.Value);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    public Task<IReadOnlyList<FileDiff>> ReadBetweenAsync(
        string workingDirectory,
        string fromRevision,
        string toRevision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(toRevision);

        options ??= DiffOptions.Default;

        List<string> arguments = ["diff"];

        AddFormatArguments(arguments, options);
        arguments.Add(fromRevision);
        arguments.Add(toRevision);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    public Task<IReadOnlyList<FileDiff>> ReadAgainstWorkingTreeAsync(
        string workingDirectory,
        string revision,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        options ??= DiffOptions.Default;

        List<string> arguments = ["diff"];

        AddFormatArguments(arguments, options);
        arguments.Add(revision);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    public Task<IReadOnlyList<FileDiff>> ReadUnstagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        options ??= DiffOptions.Default;

        List<string> arguments = ["diff"];

        AddFormatArguments(arguments, options);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    public Task<IReadOnlyList<FileDiff>> ReadStagedAsync(
        string workingDirectory,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        options ??= DiffOptions.Default;

        List<string> arguments = ["diff", "--cached"];

        AddFormatArguments(arguments, options);
        arguments.Add("--");

        return RunAsync(workingDirectory, arguments, options, cancellationToken);
    }

    /// <summary>
    /// Ayrıştırıcının beklediği biçimi kuran ortak argümanlar.
    /// </summary>
    /// <remarks>
    /// <c>--raw -z</c> yolları ham (tırnaksız) verir, <c>--patch</c> hunk'ları ekler.
    /// İkisi tek çağrıda alınıyor; ayrı çağrılar iki ayrı süreç ve iki ayrı anlık görüntü
    /// demek olurdu (çalışma dizini bu arada değişebilir).
    /// </remarks>
    private static void AddFormatArguments(List<string> arguments, DiffOptions options)
    {
        arguments.Add("--raw");

        // İçerik üretmeden dosya başına değişen satır sayısını verir; boyut koruması buna
        // dayanıyor. Ölçüldü: aynı çağrıya eklemek ek maliyet getirmiyor (20,5 vs 21,4 ms).
        arguments.Add("--numstat");

        arguments.Add("-z");
        arguments.Add("--patch");

        // İkisi de AÇIKÇA geçiliyor: `-M`'i atlamak tespiti kapatmıyor (varsayılan açık),
        // ve kullanıcının `diff.renames` ayarı davranışımızı değiştirmemeli.
        arguments.Add(options.DetectRenames
            ? options.RenameThreshold is { } threshold
                ? $"-M{Clamp(threshold)}%"
                : "-M"
            : "--no-renames");

        if (options.DetectCopies)
        {
            arguments.Add(options.CopyThreshold is { } copyThreshold
                ? $"-C{Clamp(copyThreshold)}%"
                : "-C");

            // ÖLÇÜLDÜ: `-C` tek başına, DEĞİŞTİRİLMEMİŞ bir dosyadan yapılan kopyayı
            // bulamıyor. Kullanıcı kopya tespiti istediyse ama bunu açmadıysa çoğu kopya
            // görünmez kalır — bu yüzden ayrı ve açık bir seçenek.
            if (options.FindCopiesHarder)
            {
                arguments.Add("--find-copies-harder");
            }
        }

        if (options.ContextLines is { } context)
        {
            arguments.Add($"-U{Math.Max(context, 0)}");
        }

        switch (options.Whitespace)
        {
            case WhitespaceMode.IgnoreChange:
                arguments.Add("-b");
                break;

            case WhitespaceMode.IgnoreAll:
                arguments.Add("-w");
                break;

            default:
                break;
        }

        if (options.IgnoreBlankLines)
        {
            arguments.Add("--ignore-blank-lines");
        }

    }

    /// <summary>Eşik yüzdesini git'in kabul ettiği aralığa sıkıştırır.</summary>
    private static int Clamp(int percent) => Math.Clamp(percent, 1, 100);

    private async Task<IReadOnlyList<FileDiff>> RunAsync(
        string workingDirectory,
        List<string> arguments,
        DiffOptions options,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = arguments,
                MaximumOutputBytes = options.MaximumOutputBytes > 0 ? options.MaximumOutputBytes : null,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.OutputTruncated)
        {
            // Yarım çıktıyı ayrıştırmak sessizce eksik diff göstermek olurdu. İçeriksiz
            // ikinci bir okuma yapılıp dosya listesi yine de gösteriliyor.
            return await ReadMetadataOnlyAsync(workingDirectory, arguments, cancellationToken)
                .ConfigureAwait(false);
        }

        return DiffParser.Parse(
            result.GetStandardOutputLossless(),
            options.WordLevel,
            options.MaximumChangedLines,
            options.ContentEncoding);
    }

    /// <summary>
    /// Yalnızca dosya listesi ve satır sayıları — yama istenmez.
    /// </summary>
    /// <remarks>
    /// Çıktı sınırı aşıldığında kullanılır. Kullanıcı hangi dosyaların değiştiğini yine
    /// görür; içerik "çok büyük" olarak işaretlenir.
    /// </remarks>
    private async Task<IReadOnlyList<FileDiff>> ReadMetadataOnlyAsync(
        string workingDirectory,
        List<string> arguments,
        CancellationToken cancellationToken)
    {
        List<string> metadata = [.. arguments.Where(a => a != "--patch")];

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand { WorkingDirectory = workingDirectory, Arguments = metadata },
            cancellationToken).ConfigureAwait(false);

        // Sınır 1: her dosya "çok büyük" sayılır, çünkü hangisinin taşırdığını bilmiyoruz.
        return DiffParser.Parse(
            result.GetStandardOutputLossless(), inlineSegments: false, maximumChangedLines: 1);
    }
}
