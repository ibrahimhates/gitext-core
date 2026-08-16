using System.Collections;
using System.Diagnostics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using GitExt.Benchmarks;

/// <summary>
/// The Phase 09 benchmark runner.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
///   # Run every benchmark (statistically meaningful results)
///   dotnet run --project benchmarks/GitExt.Benchmarks -c Release
///
///   # A single class — the pattern matches part of the class name
///   dotnet run --project benchmarks/GitExt.Benchmarks -c Release -- --filter "*Linear*"
///
///   # A single method — `Class.Method`
///   dotnet run --project benchmarks/GitExt.Benchmarks -c Release -- --filter "LinearBenchmarks.Linear_50k"
///
///   # A quick smoke test — 1 warmup, 3 iterations
///   dotnet run --project benchmarks/GitExt.Benchmarks -c Release -- --fast --filter "*Parser*"
/// </code>
/// <para>
/// The filter takes a single pattern; for several classes the command is repeated. The <c>*</c> in the
/// pattern is for readability — the matching is done with "contains" semantics in any case.
/// </para>
/// The results are written to `BenchmarkDotNet.Artifacts/results/` as JSON, XML and Markdown.
/// </remarks>
internal class Program
{
    /// <summary>The default logger + exporter set (compatible with BenchmarkDotNet v0.15.6).</summary>
    private static readonly IExporter[] DefaultExporters =
    [
        PlainExporter.Default,
        MarkdownExporter.GitHub,
        BenchmarkDotNet.Exporters.DefaultExporters.JsonBrief,
        BenchmarkDotNet.Exporters.DefaultExporters.XmlBrief,
    ];

    /// <summary>All benchmark types in this assembly.</summary>
    private static readonly Type[] AllBenchmarkTypes =
    [
        typeof(BranchedBenchmarks),
        typeof(LinearBenchmarks),
        typeof(MergedBbenchmarks),
        typeof(ModelBenchmarks),
        typeof(MultiMergeBenchmarks),
        typeof(ParserBenchmarks),
        typeof(RenderCacheBenchmarks),
    ];

    private static int Main(string[] args)
    {
        IConfig config = CreateConfig(args);
        var filterIndex = Array.IndexOf(args, "--filter");
        string? filterPattern = (filterIndex >= 0 && filterIndex + 1 < args.Length)
            ? args[filterIndex + 1] : null;

        // Apply optional filter.
        Type[] typesToRun = filterPattern is not null && filterPattern.Trim('*').Length > 0
            ? AllBenchmarkTypes.Where(t => MatchesType(t.Name, filterPattern)).ToArray()
            : AllBenchmarkTypes;

        if (typesToRun.Length == 0)
        {
            Console.Error.WriteLine(filterPattern is null
                ? "No benchmark types found."
                : $"No benchmark types match filter '{filterPattern}'.");
            return 1;
        }

        // Run each type sequentially.
        int failed = 0;
        double grandTotalMs = 0;
        var sw = Stopwatch.StartNew();

        // Run all types in one call. Pass empty string[] so BDN's own parser
        // doesn't see our custom flags (--fast). Filter is handled by us above.
        Summary[] summaries = BenchmarkRunner.Run(typesToRun, config, args.Length == 0 ? null : new string[0]);

        foreach (var summary in summaries)
        {
            Console.WriteLine($"\n{'=',-60}");
            // Get the benchmark type name from the first case's display info.
            var cases = summary.BenchmarksCases;
            if (cases.Length > 0)
            {
                string name = cases[0].Descriptor.DisplayInfo;
                int dotIdx = name.IndexOf('.');
                if (dotIdx > 0) name = name.Substring(0, dotIdx);
                Console.WriteLine($"Running: {name}");
            }
            Console.WriteLine($"{'=',-60}");

            ProcessSummary(summary, ref failed, ref grandTotalMs);
        }

        sw.Stop();

        if (failed > 0)
        {
            Console.Error.WriteLine($"\n{failed} benchmark(ler) başarısız oldu.");
            return 1;
        }

        Console.WriteLine($"\nToplam geçen: {sw.ElapsedMilliseconds:F0} ms");
        Console.WriteLine($"Benchmark süresi (Mean toplam): {grandTotalMs:F0} ms");

        return 0;
    }

    private static void ProcessSummary(Summary summary, ref int failed, ref double totalMs)
    {
        // Iterate reports via non-generic IEnumerable + dynamic casting.
        var reports = ((IEnumerable)summary.Reports).Cast<dynamic>().ToList();

        foreach (var report in reports)
        {
            if (!(bool)(bool)report.BuildResult.IsBuildSuccess || !(bool)report.Success)
                failed++;
        }

        // Sum Mean metric (nanoseconds → milliseconds).
        foreach (var report in reports)
        {
            var metricsDict = ((IDictionary)report.Metrics);
            foreach (DictionaryEntry kvp in metricsDict)
            {
                if (kvp.Key is string key && key == "Mean" && kvp.Value is not null)
                {
                    // Metric.TotalTime is a double.
                    var metricObj = kvp.Value!;
                    double meanNs = Convert.ToDouble(metricObj.GetType().GetProperty("TotalTime")?.GetValue(metricObj) ?? 0.0);
                    totalMs += meanNs / 1_000_000;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Whether a class name matches the filter pattern.
    /// </summary>
    /// <remarks>
    /// The pattern can use wildcards such as <c>*Graph*</c>, or take the form <c>Class.Method</c>. For
    /// class selection only the class part is considered; the method part is picked out by
    /// BenchmarkDotNet's own filter. Compared as raw text in its entirety, a <c>*</c> would match no
    /// class at all and the run would silently come back empty.
    /// </remarks>
    private static bool MatchesType(string typeName, string pattern)
    {
        string typePart = pattern.Split('.')[0].Trim('*');

        return typePart.Length == 0
            || typeName.Contains(typePart, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfig CreateConfig(string[] args)
    {
        bool fastMode = args.Contains("--fast");
        if (fastMode)
            Console.WriteLine("[fast] Warmup=1, Iteration=3");

        var builder = new ManualConfig();
        // ConsoleLogger(bool, Dictionary<LogKind, ConsoleColor>?) — header=true, colors=default.
        builder.AddLogger(new ConsoleLogger(true, null));
        builder.AddExporter(DefaultExporters);

        // --fast mode: a quick run for the CI smoke test.
        if (fastMode)
        {
            var fastJob = Job.Default
                .WithIterationCount(3)
                .WithWarmupCount(1);
            builder.AddJob(fastJob);
        }

        // Method-level matching is only done when the pattern takes the `Class.Method` form.
        // A pattern without a dot (`*Linear*`) targets the class name — Main filters on that; applied
        // here as well, nothing would run because it has no counterpart among the method names.
        var filterIndex = Array.IndexOf(args, "--filter");
        if (filterIndex >= 0 && filterIndex + 1 < args.Length)
        {
            string[] parts = args[filterIndex + 1].Split('.');

            if (parts.Length > 1)
            {
                string methodPart = parts[^1].Trim('*');

                if (methodPart.Length > 0)
                {
                    builder.AddFilter(new NameFilter(
                        name => name.Contains(methodPart, StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        return builder;
    }
}
