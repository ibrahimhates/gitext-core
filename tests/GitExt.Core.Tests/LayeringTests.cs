using System.Reflection;

namespace GitExt.Core.Tests;

/// <summary>
/// P01-T11 / ADR-0003 — Verifying the layering rule at run time as well.
/// </summary>
/// <remarks>
/// The real protection is the MSBuild target in <c>build/NoUiDependencies.props</c> and it runs at
/// build time. This test is a second seat belt: if someone removes or skips the MSBuild target it is
/// caught here.
/// </remarks>
public class LayeringTests
{
    [Fact]
    public void Core_hicbir_UI_derlemesine_bagimli_degildir()
    {
        AssemblyName[] referenced = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies();

        string[] forbidden = [.. referenced
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
                     || n.StartsWith("SkiaSharp", StringComparison.OrdinalIgnoreCase)
                     || n.StartsWith("HarfBuzzSharp", StringComparison.OrdinalIgnoreCase)
                     || n.Equals("ReactiveUI", StringComparison.OrdinalIgnoreCase))];

        forbidden.ShouldBeEmpty(
            "GitExt.Core must stay independent of the UI (docs/adr/0003-solution-structure.md). Found: "
            + string.Join(", ", forbidden));
    }
}
