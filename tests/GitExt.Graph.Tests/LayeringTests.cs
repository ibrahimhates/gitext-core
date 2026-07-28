using System.Reflection;

namespace GitExt.Graph.Tests;

/// <summary>
/// ADR-0003 — <c>GitExt.Graph</c> yerleşim algoritmasını barındırır ve UI'dan bağımsız kalmalıdır.
/// Bu bağımsızlık, Faz 03'te algoritmanın çizim yapmadan test edilebilmesini sağlar.
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
