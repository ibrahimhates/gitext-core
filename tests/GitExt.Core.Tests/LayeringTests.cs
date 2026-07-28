using System.Reflection;

namespace GitExt.Core.Tests;

/// <summary>
/// P01-T11 / ADR-0003 — Katman kuralının çalışma zamanında da doğrulanması.
/// </summary>
/// <remarks>
/// Asıl koruma <c>build/NoUiDependencies.props</c> içindeki MSBuild hedefidir ve derleme
/// zamanında çalışır. Bu test ikinci bir emniyet kemeridir: birisi MSBuild hedefini kaldırırsa
/// veya atlarsa burada yakalanır.
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
