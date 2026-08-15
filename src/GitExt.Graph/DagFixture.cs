using System.Text;

namespace GitExt.Graph;

/// <summary>
/// Text-based DAG definition for tests (P03-T02).
/// </summary>
/// <remarks>
/// <para>
/// So the layout algorithm can be tested with readable scenarios, without setting up a real repository.
/// Setting up a fixture with real <c>git</c> (the Phase 02 approach) does not fit here: describing the
/// DAG that produces a lane collision with <c>git</c> commands is far harder than describing the same
/// thing in four lines of text, and when you read it you cannot tell what is being tested.
/// </para>
/// <para>
/// Format — one commit per line, <b>newest to oldest</b> (<c>git log</c> order):
/// </para>
/// <code>
/// D: B C     # D's parents are B and C (merge)
/// C: A
/// B: A
/// A:         # root commit, no parents
/// </code>
/// <para>
/// Rules:
/// </para>
/// <list type="bullet">
///   <item>Lines starting with <c>#</c> and blank lines are ignored.</item>
///   <item>A trailing <c>#</c> comment is dropped as well.</item>
///   <item>Parents may be separated by a space or a comma.</item>
///   <item>Ids are free text; single letters are recommended so they stay readable.</item>
/// </list>
/// </remarks>
public static class DagFixture
{
    /// <summary>
    /// Converts the text definition into a commit list.
    /// </summary>
    /// <exception cref="FormatException">
    /// If a line cannot be parsed, an id is repeated, or a commit points at a parent that was not
    /// defined <b>before</b> it.
    /// </exception>
    public static IReadOnlyList<DagCommit> Parse(string definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        List<DagCommit> commits = [];
        HashSet<string> seen = [];
        int lineNumber = 0;

        foreach (string rawLine in definition.Split('\n'))
        {
            lineNumber++;

            string line = StripComment(rawLine).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon < 0)
            {
                throw new FormatException(
                    $"Satır {lineNumber}: ':' bekleniyordu. Biçim: 'kimlik: ebeveyn1 ebeveyn2'. Gelen: '{line}'");
            }

            string id = line[..colon].Trim();

            if (id.Length == 0)
            {
                throw new FormatException($"Satır {lineNumber}: commit kimliği boş olamaz.");
            }

            if (!seen.Add(id))
            {
                throw new FormatException($"Satır {lineNumber}: '{id}' kimliği birden fazla kez tanımlandı.");
            }

            string[] parents = line[(colon + 1)..]
                .Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);

            commits.Add(new DagCommit(id, parents));
        }

        ValidateTopologicalOrder(commits);
        return commits;
    }

    /// <summary>
    /// Verifies that the input is in topological order: every parent must come <b>after</b> its child.
    /// </summary>
    /// <remarks>
    /// This is the invariant of ADR-0007. The algorithm does a single forward pass; if a parent comes
    /// before its child the edge points upwards and the graph breaks. If the fixture itself makes this
    /// mistake the test verifies the wrong thing — that is why it is caught here.
    /// <para>
    /// Undefined parents are allowed: partial history (a paging limit) looks like this.
    /// </para>
    /// </remarks>
    private static void ValidateTopologicalOrder(IReadOnlyList<DagCommit> commits)
    {
        Dictionary<string, int> position = [];

        for (int i = 0; i < commits.Count; i++)
        {
            position[commits[i].Id] = i;
        }

        for (int i = 0; i < commits.Count; i++)
        {
            foreach (string parent in commits[i].Parents)
            {
                // Undefined parent = the point where history was cut off, not a problem.
                if (position.TryGetValue(parent, out int parentIndex) && parentIndex < i)
                {
                    throw new FormatException(
                        $"Topolojik sıra ihlali: '{parent}' ebeveyni, çocuğu '{commits[i].Id}' "
                        + $"commit'inden ÖNCE tanımlanmış (satır {parentIndex + 1} < {i + 1}). "
                        + "Girdi en yeniden en eskiye sıralı olmalı (ADR-0007).");
                }
            }
        }
    }

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? line : line[..hash];
    }

    /// <summary>
    /// Converts the layout result into a text table that can be compared against the expected output.
    /// </summary>
    /// <remarks>
    /// So that tests can write the expected value as a readable string instead of building it by hand.
    /// When a test breaks, the difference is visible at a glance.
    /// </remarks>
    public static string Render(IReadOnlyList<GraphRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        StringBuilder builder = new();

        foreach (GraphRow row in rows)
        {
            builder.Append(row.Commit.Id).Append(": şerit=").Append(row.Lane);

            if (row.Edges.Count > 0)
            {
                builder.Append(" kenarlar=");
                builder.AppendJoin(
                    ' ',
                    row.Edges.Select(e => $"{e.FromLane}→{e.ToLane}({e.Target})"));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }
}

/// <summary>
/// A single commit coming from the fixture — id and parents only.
/// </summary>
/// <remarks>
/// The layout algorithm knows nothing about author, date or message; since it does not need them,
/// this narrowed type is used instead of <see cref="Core.Model.CommitInfo"/>. That way the
/// algorithm tests do not have to build a full commit.
/// </remarks>
public sealed record DagCommit(string Id, IReadOnlyList<string> Parents)
{
    public bool IsMerge => Parents.Count > 1;

    public bool IsRoot => Parents.Count == 0;

    public override string ToString() =>
        Parents.Count == 0 ? Id : $"{Id} → {string.Join(", ", Parents)}";
}
