namespace GitExt.Graph;

/// <summary>
/// Marker type used to reference this assembly.
/// </summary>
/// <remarks>
/// The commit DAG layout algorithm (lane assignment, edge routing, color assignment) will land here.
/// This project is deliberately independent of the UI; that way the algorithm can be tested without
/// drawing anything. See <c>docs/adr/0003-solution-structure.md</c>.
/// </remarks>
public static class AssemblyMarker;
