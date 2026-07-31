using System.Diagnostics;
using System.Globalization;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Graph;

namespace GitExt.Desktop;

/// <summary>
/// Bir depoyu commit grafiği hattından geçirip ölçer (P03-T18).
/// </summary>
/// <remarks>
/// <para>
/// Kullanım: <c>gitext-core --bench &lt;depo-yolu&gt;</c>
/// </para>
/// <para>
/// <b>Neden ayrı bir mod?</b> Faz 02'nin ölçümleri yalnızca çekirdek katmanı kapsıyordu.
/// Burada ölçülen şey kullanıcının beklediği şey: <b>ilk satır ne zaman görünür</b>,
/// tamamı ne kadar sürer, kaç şerit oluşur ve bellekte ne kalır. Avalonia başlatılmaz —
/// masaüstü oturumu gerekmez, CI'da da çalışır.
/// </para>
/// <para>
/// Tutulan bellek, satırlar <b>canlı tutularak</b> ve zorlanmış bir çöp toplama sonrası
/// ölçülür; aksi halde ölçülen şey tutulan değil tahsis edilen bellek olur — Faz 03'te bir
/// kez bu hataya düşüldü.
/// </para>
/// </remarks>
internal static class GraphBenchmark
{
    internal const string Flag = "--bench";

    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                      ?? Directory.GetCurrentDirectory();

        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        GitProcessRunner runner = new(executable);

        RepositoryLocation location = await new RepositoryLocator(runner)
            .LocateAsync(path, cancellationToken).ConfigureAwait(false);

        // Tüm ref'ler mi yalnızca HEAD mi? Şerit genişliğinin kaynağını ayırmak için
        // (bir depoda 1000'den fazla tag olabiliyor — ölçüldü).
        bool headOnly = args.Contains("--head-only", StringComparer.Ordinal);

        Console.WriteLine($"depo        : {location.WorkingDirectory}");
        Console.WriteLine($"kapsam      : {(headOnly ? "yalnızca HEAD" : "tüm ref'ler (--all)")}");

        await MeasureAsync(runner, location.WorkingDirectory, headOnly, cancellationToken)
            .ConfigureAwait(false);

        return 0;
    }

    private static async Task MeasureAsync(
        IGitProcessRunner runner,
        string workingDirectory,
        bool headOnly,
        CancellationToken cancellationToken)
    {
        CommitLogReader reader = new(runner);
        CommitLogQuery query = new() { IncludeAllRefs = !headOnly };

        GraphLayoutEngine engine = new();

        // Satırlar canlı tutuluyor: tutulan belleği ölçmenin tek yolu bu.
        List<GraphRow> rows = [];
        List<CommitInfo> commits = [];

        long beforeBytes = GetRetainedBytes();

        Stopwatch total = Stopwatch.StartNew();
        TimeSpan firstRow = TimeSpan.Zero;

        // Şerit sayısının DAĞILIMI, tepe değerinden daha çok şey söylüyor: birkaç satırda
        // görülen bir tepe yatay kaydırmayla çözülür, yaygın bir genişlik ise algoritmayı
        // sorgulatır (P03-T18, açık soru 1 ve 2).
        List<int> laneCounts = [];

        // Asıl soru sütun genişliği için bu: commit DÜĞÜMLERİ hangi şeritlerde duruyor?
        // Şeritlerin çoğu uzun geçiş kenarıysa dar bir sınır düğümleri gizlemez (P03-T21).
        List<int> nodeLanes = [];

        // Şeritler gerçekten dolu mu, yoksa aralarında boşluk mu var? Boşluk varsa çözüm
        // sıkıştırma (compaction); doluysa eşzamanlı dal sayısı gerçekten yüksek demektir.
        List<int> occupied = [];

        await foreach (CommitInfo commit in reader
                           .StreamAsync(workingDirectory, query, cancellationToken)
                           .ConfigureAwait(false))
        {
            GraphRow row = engine.Add(ToDagCommit(commit));

            if (rows.Count == 0)
            {
                firstRow = total.Elapsed;
            }

            rows.Add(row);
            commits.Add(commit);
            laneCounts.Add(row.LaneCount);
            nodeLanes.Add(row.Lane);

            HashSet<int> used = [row.Lane];

            foreach (GraphEdge edge in row.Edges)
            {
                used.Add(edge.FromLane);
                used.Add(edge.ToLane);
            }

            occupied.Add(used.Count);
        }

        total.Stop();

        long afterBytes = GetRetainedBytes();

        // rows/commits'e burada dokunmak, JIT'in listeleri erken toplanabilir saymasını önler.
        int count = rows.Count + commits.Count - commits.Count;

        Report(count, firstRow, total.Elapsed, laneCounts, afterBytes - beforeBytes);
        ReportNodeLanes(nodeLanes);
        ReportOccupancy(laneCounts, occupied);
        ReportTextWeight(commits);

        GC.KeepAlive(rows);
        GC.KeepAlive(commits);
    }

    private static void Report(
        int rowCount,
        TimeSpan firstRow,
        TimeSpan total,
        List<int> laneCounts,
        long retainedBytes)
    {
        CultureInfo c = CultureInfo.InvariantCulture;

        Console.WriteLine($"commit      : {rowCount.ToString("N0", c)}");
        Console.WriteLine($"ilk satır   : {firstRow.TotalMilliseconds.ToString("F1", c)} ms");
        Console.WriteLine($"tamamı      : {total.TotalMilliseconds.ToString("F0", c)} ms");

        if (total.TotalSeconds > 0)
        {
            double perSecond = rowCount / total.TotalSeconds;
            Console.WriteLine($"hız         : {perSecond.ToString("N0", c)} commit/sn");
        }

        laneCounts.Sort();

        Console.WriteLine(
            "şerit       : "
            + $"p50={Percentile(laneCounts, 0.50)}  "
            + $"p90={Percentile(laneCounts, 0.90)}  "
            + $"p99={Percentile(laneCounts, 0.99)}  "
            + $"max={(laneCounts.Count > 0 ? laneCounts[^1] : 0)}");

        double megabytes = retainedBytes / 1024.0 / 1024.0;
        Console.WriteLine($"tutulan     : {megabytes.ToString("F0", c)} MB");

        if (rowCount > 0)
        {
            double perRow = (double)retainedBytes / rowCount;
            Console.WriteLine($"satır başına: {perRow.ToString("F0", c)} bayt");
        }
    }

    /// <summary>
    /// Commit düğümlerinin şerit indeksi dağılımı ve olası sınırların kapsama oranı.
    /// </summary>
    private static void ReportNodeLanes(List<int> nodeLanes)
    {
        nodeLanes.Sort();

        Console.WriteLine(
            "düğüm şeridi: "
            + $"p50={Percentile(nodeLanes, 0.50)}  "
            + $"p90={Percentile(nodeLanes, 0.90)}  "
            + $"p99={Percentile(nodeLanes, 0.99)}  "
            + $"max={(nodeLanes.Count > 0 ? nodeLanes[^1] : 0)}");

        foreach (int cap in new[] { 8, 12, 16, 24, 32 })
        {
            int visible = nodeLanes.Count(l => l < cap);
            double share = nodeLanes.Count == 0 ? 0 : 100.0 * visible / nodeLanes.Count;

            Console.WriteLine(
                $"  sınır {cap,2}   : düğümlerin %{share.ToString("F2", CultureInfo.InvariantCulture)}'i görünür");
        }
    }

    /// <summary>
    /// Ayrılan şeritlerin ne kadarının gerçekten kullanıldığı.
    /// </summary>
    private static void ReportOccupancy(List<int> laneCounts, List<int> occupied)
    {
        if (occupied.Count == 0)
        {
            return;
        }

        // laneCounts zaten sıralandı; oran için sıralanmamış eşleşme gerekli olduğundan
        // toplamlar üzerinden bakılıyor.
        double totalLanes = laneCounts.Sum(x => (double)x);
        double totalUsed = occupied.Sum(x => (double)x);

        List<int> sortedUsed = [.. occupied];
        sortedUsed.Sort();

        Console.WriteLine(
            "dolu şerit  : "
            + $"p50={Percentile(sortedUsed, 0.50)}  "
            + $"p90={Percentile(sortedUsed, 0.90)}  max={sortedUsed[^1]}  "
            + $"· doluluk %{(100 * totalUsed / totalLanes).ToString("F1", CultureInfo.InvariantCulture)}");
    }

    private static int Percentile(List<int> sorted, double fraction) =>
        sorted.Count == 0
            ? 0
            : sorted[Math.Clamp((int)(sorted.Count * fraction), 0, sorted.Count - 1)];

    /// <summary>
    /// Metin alanlarının bellekteki payını raporlar.
    /// </summary>
    /// <remarks>
    /// Belleği neyin yediğini bilmeden azaltmaya çalışmak tahmin yürütmektir. Yazar adı ve
    /// e-postası az sayıda benzersiz değere sahiptir; interning'in ne kazandıracağı buradan
    /// görülüyor (Faz 09).
    /// </remarks>
    private static void ReportTextWeight(List<CommitInfo> commits)
    {
        long subject = 0;
        long body = 0;
        long people = 0;

        HashSet<string> uniquePeople = new(StringComparer.Ordinal);
        long peopleUnique = 0;

        foreach (CommitInfo commit in commits)
        {
            subject += commit.Subject.Length;
            body += commit.Body.Length;

            people += commit.Author.Name.Length + commit.Author.Email.Length
                + commit.Committer.Name.Length + commit.Committer.Email.Length;

            foreach (string value in new[]
                     {
                         commit.Author.Name, commit.Author.Email,
                         commit.Committer.Name, commit.Committer.Email,
                     })
            {
                if (uniquePeople.Add(value))
                {
                    peopleUnique += value.Length;
                }
            }
        }

        CultureInfo c = CultureInfo.InvariantCulture;

        // .NET'te char 2 bayt.
        Console.WriteLine(
            "metin (MB)  : "
            + $"konu={Mb(subject)}  gövde={Mb(body)}  "
            + $"kişi={Mb(people)} (benzersiz {Mb(peopleUnique)}, {uniquePeople.Count.ToString("N0", c)} değer)");

        static string Mb(long chars) =>
            (chars * 2.0 / 1024 / 1024).ToString("F0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Zorlanmış tam toplama sonrası tutulan bellek.
    /// </summary>
    /// <remarks>
    /// <c>GC.GetTotalMemory(forceFullCollection: true)</c> tek başına yetmiyor; finalize
    /// bekleyen nesneler sayıya karışıyor ve ölçüm gürültülü çıkıyor.
    /// </remarks>
    private static long GetRetainedBytes()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static DagCommit ToDagCommit(CommitInfo commit)
    {
        string[] parents = new string[commit.Parents.Count];

        for (int i = 0; i < parents.Length; i++)
        {
            parents[i] = commit.Parents[i].Value;
        }

        return new DagCommit(commit.Id.Value, parents);
    }
}
