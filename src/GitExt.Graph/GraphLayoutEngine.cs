namespace GitExt.Graph;

/// <summary>
/// Lays the commit DAG out into vertical lanes (P03-T03, P03-T04).
/// </summary>
/// <remarks>
/// <para>
/// <b>Single forward pass.</b> Because commits arrive with <c>--topo-order</c>, every child is
/// processed before its parent (ADR-0007). The engine keeps state: once a row has been produced it
/// is never touched again, so adding new commits does <b>not</b> change the lanes of earlier rows —
/// that is how visual stability is achieved.
/// </para>
/// <para>
/// <b>Core idea:</b> when a commit is processed, a lane is <i>reserved</i> for its parents. A reserved
/// lane stays occupied until that parent is reached. That way a long-distance edge occupies its own
/// lane on every row it crosses and nothing else can be placed there — a collision becomes
/// structurally impossible.
/// </para>
/// <para>
/// <b>Straight lanes:</b> the first parent of a commit continues <i>in the same lane</i>. So the main
/// chain of a branch stays in a single column and can be followed by eye (ADR-0007).
/// </para>
/// </remarks>
public sealed class GraphLayoutEngine
{
    /// <summary>
    /// Lane reservation: which commit this lane is waiting for and which color it has.
    /// </summary>
    private readonly record struct LaneSlot(string Target, int ColorIndex);

    private readonly List<LaneSlot?> _lanes = [];
    private int _nextColor;

    /// <summary>The widest lane count used so far.</summary>
    public int MaxLaneCount { get; private set; }

    /// <summary>Number of rows processed.</summary>
    public int RowCount { get; private set; }

    /// <summary>
    /// Lays out a sequence of commits.
    /// </summary>
    /// <remarks>
    /// Does not reset the engine; it can be called repeatedly. This is how the next page is appended
    /// during infinite scrolling (P03-T06).
    /// </remarks>
    public IReadOnlyList<GraphRow> Add(IEnumerable<DagCommit> commits)
    {
        ArgumentNullException.ThrowIfNull(commits);

        List<GraphRow> rows = [];

        foreach (DagCommit commit in commits)
        {
            rows.Add(Add(commit));
        }

        return rows;
    }

    /// <summary>
    /// Lays out a single commit and produces its row.
    /// </summary>
    public GraphRow Add(DagCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        // 1. Find the lanes already reserved for this commit.
        //    If there is more than one, this commit is the shared parent of several children
        //    (a branch point); they all join on this row.
        List<int> reserved = FindReservedLanes(commit.Id);

        int lane;
        int color;

        if (reserved.Count == 0)
        {
            // None of its children have been processed: this is a branch tip. Open a new lane.
            lane = AllocateLane();
            color = NextColor();
        }
        else
        {
            // Use the leftmost reservation — keep the branch on the left, open new branches to the right.
            lane = reserved[0];
            color = _lanes[lane]!.Value.ColorIndex;

            // The other reservations end on this row; release their lanes.
            for (int i = 1; i < reserved.Count; i++)
            {
                _lanes[reserved[i]] = null;
            }
        }

        // 2. Reserve lanes for the parents.
        //    The first parent continues in the SAME lane — that is the "straight lane" rule.
        List<GraphEdge> edges = [];

        if (commit.Parents.Count == 0)
        {
            // Root commit: the chain ends here, the lane is freed.
            _lanes[lane] = null;
        }
        else
        {
            // If the first parent is already awaited in ANOTHER lane, this lane ends here and the
            // edge connects to it diagonally.
            //
            // FROM REAL DATA: several topic branches opened off the same base
            // (e.g. three dependabot branches, all parented at `init`) would otherwise each reserve
            // their own lane down to the base, and the graph gets needlessly wide.
            // Our own repository produced 4 lanes; git's own graph makes do with 2.
            //
            // It does not break the "straight lane" rule: the branch really does join here.
            int existingFirst = FindReservedLanes(commit.Parents[0]).FirstOrDefault(-1);

            if (existingFirst >= 0 && existingFirst != lane)
            {
                edges.Add(new GraphEdge
                {
                    FromLane = lane,
                    ToLane = existingFirst,
                    Target = commit.Parents[0],
                    ColorIndex = _lanes[existingFirst]!.Value.ColorIndex,
                });

                _lanes[lane] = null;
            }
            else
            {
                _lanes[lane] = new LaneSlot(commit.Parents[0], color);

                edges.Add(new GraphEdge
                {
                    FromLane = lane,
                    ToLane = lane,
                    Target = commit.Parents[0],
                    ColorIndex = color,
                });
            }

            // Extra parents (merge): each one gets a new lane.
            for (int i = 1; i < commit.Parents.Count; i++)
            {
                string parent = commit.Parents[i];

                // If this parent is already awaited in a lane (another child reserved it), do not open
                // a new lane — connect to the existing reservation. Otherwise two lanes are created for
                // the same commit and the graph gets needlessly wide.
                int existing = FindReservedLanes(parent).FirstOrDefault(-1);

                if (existing >= 0)
                {
                    edges.Add(new GraphEdge
                    {
                        FromLane = lane,
                        ToLane = existing,
                        Target = parent,
                        ColorIndex = _lanes[existing]!.Value.ColorIndex,
                    });

                    continue;
                }

                int mergeLane = AllocateLane();
                int mergeColor = NextColor();
                _lanes[mergeLane] = new LaneSlot(parent, mergeColor);

                edges.Add(new GraphEdge
                {
                    FromLane = lane,
                    ToLane = mergeLane,
                    Target = parent,
                    ColorIndex = mergeColor,
                });
            }
        }

        // 3. Lanes that pass through this row but have nothing to do with this commit.
        //    The render layer will draw them as a straight line with no node.
        for (int i = 0; i < _lanes.Count; i++)
        {
            if (i == lane || _lanes[i] is not { } slot)
            {
                continue;
            }

            // If it has already been handled by an edge on this row, do not add it again.
            if (edges.Any(e => e.ToLane == i))
            {
                continue;
            }

            edges.Add(new GraphEdge
            {
                FromLane = i,
                ToLane = i,
                Target = slot.Target,
                ColorIndex = slot.ColorIndex,
                IsPassThrough = true,
            });
        }

        TrimTrailingFreeLanes();

        int laneCount = Math.Max(_lanes.Count, lane + 1);
        MaxLaneCount = Math.Max(MaxLaneCount, laneCount);
        RowCount++;

        return new GraphRow
        {
            Commit = commit,
            Lane = lane,
            ColorIndex = color,
            Edges = edges,
            LaneCount = laneCount,
        };
    }

    private List<int> FindReservedLanes(string commitId)
    {
        List<int> result = [];

        for (int i = 0; i < _lanes.Count; i++)
        {
            if (_lanes[i] is { } slot && string.Equals(slot.Target, commitId, StringComparison.Ordinal))
            {
                result.Add(i);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the leftmost free lane; if there is none, adds a new lane.
    /// </summary>
    /// <remarks>
    /// Filling from the left keeps the graph narrow. Freed lanes are reused — this does not break the
    /// "straight lane" rule, because a lane is only given to someone else after it has genuinely been
    /// released.
    /// </remarks>
    private int AllocateLane()
    {
        for (int i = 0; i < _lanes.Count; i++)
        {
            if (_lanes[i] is null)
            {
                return i;
            }
        }

        _lanes.Add(null);
        return _lanes.Count - 1;
    }

    /// <summary>
    /// Drops trailing empty lanes so the graph does not look needlessly wide.
    /// </summary>
    private void TrimTrailingFreeLanes()
    {
        int last = _lanes.Count - 1;

        while (last >= 0 && _lanes[last] is null)
        {
            _lanes.RemoveAt(last);
            last--;
        }
    }

    /// <summary>
    /// Picks the smallest color index that is not currently in use.
    /// </summary>
    /// <remarks>
    /// Goal: lanes visible at the same time should have different colors. Palette size and the real
    /// colors are the theme layer's job (Phase 08); only distinguishability is guaranteed here.
    /// </remarks>
    private int NextColor()
    {
        HashSet<int> inUse = [];

        foreach (LaneSlot? slot in _lanes)
        {
            if (slot is { } value)
            {
                inUse.Add(value.ColorIndex);
            }
        }

        for (int candidate = 0; candidate < inUse.Count + 1; candidate++)
        {
            if (!inUse.Contains(candidate))
            {
                return candidate;
            }
        }

        return _nextColor++;
    }
}
