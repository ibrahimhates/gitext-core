using System.Reflection;

namespace GitExt.Graph.Tests;

/// <summary>
/// ADR-0003 — <c>GitExt.Graph</c> hosts the layout algorithm and must stay independent of the UI.
/// That independence is what lets the algorithm be tested in Phase 03 without drawing anything.
/// </summary>
public class LayeringTests
{
    [Fact]
    public void Graph_hicbir_UI_derlemesine_bagimli_degildir()
    {
        AssemblyName[] referenced = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies();

        string[] forbidden = [.. referenced
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
                     || n.StartsWith("SkiaSharp", StringComparison.OrdinalIgnoreCase)
                     || n.StartsWith("HarfBuzzSharp", StringComparison.OrdinalIgnoreCase)
                     || n.Equals("ReactiveUI", StringComparison.OrdinalIgnoreCase))];

        forbidden.ShouldBeEmpty(
            "GitExt.Graph must stay independent of the UI (docs/adr/0003-solution-structure.md). Found: "
            + string.Join(", ", forbidden));
    }
}
