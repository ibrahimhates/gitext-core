namespace GitExt.Core;

/// <summary>
/// Marker type used to reference this assembly.
/// It exists so that tests and DI scans can say <c>typeof(AssemblyMarker).Assembly</c>.
/// </summary>
/// <remarks>
/// This project is currently a skeleton; the real Git code (process runner, command wrappers,
/// output parsers) will be added in the next stage. See <c>docs/adr/0002-git-backend.md</c>.
/// </remarks>
public static class AssemblyMarker;
