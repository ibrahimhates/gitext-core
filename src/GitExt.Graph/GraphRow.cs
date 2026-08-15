namespace GitExt.Graph;

/// <summary>
/// The result the layout algorithm produces for a single row.
/// </summary>
/// <remarks>
/// <b>Pure data</b> (ADR-0003): contains no pixels, color codes or drawing objects. The lane index
/// and the color index are abstract numbers; turning them into coordinates and colors is the render
/// layer's job. Thanks to this separation the algorithm can be tested without drawing anything.
/// </remarks>
public sealed record GraphRow
{
    /// <summary>The commit on this row.</summary>
    public required DagCommit Commit { get; init; }

    /// <summary>
    /// The vertical lane the commit node sits in; 0 is the leftmost.
    /// </summary>
    public required int Lane { get; init; }

    /// <summary>
    /// The color index assigned to the lane.
    /// </summary>
    /// <remarks>
    /// Turning it into a real color is the theme layer's job (Phase 08). The only guarantee here is
    /// that "neighboring lanes get different indexes".
    /// </remarks>
    public required int ColorIndex { get; init; }

    /// <summary>
    /// The edges reaching downwards (to older commits) from this row.
    /// </summary>
    public required IReadOnlyList<GraphEdge> Edges { get; init; }

    /// <summary>
    /// The total number of lanes in use on this row.
    /// </summary>
    /// <remarks>
    /// So the render layer knows the row width; it does not have to scan every row to find the
    /// widest point of the graph.
    /// </remarks>
    public required int LaneCount { get; init; }

    public override string ToString() => $"{Commit.Id}@{Lane}";
}

/// <summary>
/// The geometry of the connection between two rows.
/// </summary>
/// <remarks>
/// An edge <b>starts at this row and reaches downwards</b>. On a vertical edge <see cref="FromLane"/>
/// and <see cref="ToLane"/> are the same; on an edge that changes lane (a branch or a merge) they
/// differ.
/// </remarks>
public sealed record GraphEdge
{
    /// <summary>The lane the edge starts in on this row.</summary>
    public required int FromLane { get; init; }

    /// <summary>The lane the edge ends in on the next row.</summary>
    public required int ToLane { get; init; }

    /// <summary>The commit the edge reaches (the parent).</summary>
    public required string Target { get; init; }

    /// <summary>The color index assigned to the lane.</summary>
    public required int ColorIndex { get; init; }

    /// <summary>
    /// Does the edge leave a node on this row, or is it just passing through?
    /// </summary>
    /// <remarks>
    /// Pass-through edges are connections that have nothing to do with the commit on that row but
    /// still occupy a lane. The render layer draws them as a straight line with no node.
    /// </remarks>
    public bool IsPassThrough { get; init; }

    /// <summary>An edge that changes lane (diagonal)?</summary>
    public bool IsDiagonal => FromLane != ToLane;

    public override string ToString() => $"{FromLane}→{ToLane} ({Target})";
}
