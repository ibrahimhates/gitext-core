using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// Snapshot and diff of remote-tracking branches and tags (P06-T06).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate class?</b> It has two callers (<see cref="FetchWriter"/> and
/// <see cref="PullWriter"/>) and both ask the same question: <i>"which refs changed after this
/// network operation?"</i>. Two copy-pasted implementations would mean one of them silently
/// behaving differently — the lesson of P06-T04 (and what actually happened to us in
/// <c>RefReader</c> in P06-T05).
/// </para>
/// <para>
/// 🔴 The <c>%(symref)</c> field is mandatory: <c>refs/remotes/origin/HEAD</c> is <b>symbolic</b>
/// and tracks <c>origin/main</c>; because <c>%(objectname)</c> resolves it, every time main was
/// updated it showed up as a second "change" (measured).
/// </para>
/// </remarks>
internal static class RefSnapshot
{
    private const string Format = "%(refname)%00%(objectname)%00%(symref)";

    /// <summary>Remote-tracking branches and tags: ref name → commit.</summary>
    internal static async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        IGitProcessRunner runner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await runner.RunCheckedAsync(
            GitCommand.Create(
                workingDirectory,
                "for-each-ref",
                $"--format={Format}",
                "refs/remotes",
                "refs/tags"),
            cancellationToken).ConfigureAwait(false);

        Dictionary<string, string> refs = [];

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split('\0');

            // If the third field is non-empty the ref is symbolic; it is skipped because it is an alias.
            if (fields.Length == 3 && fields[2].Length == 0)
            {
                refs[fields[0]] = fields[1];
            }
        }

        return refs;
    }

    /// <summary>Returns the difference between two snapshots, ordered by ref name.</summary>
    internal static IReadOnlyList<RefChange> Diff(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        List<RefChange> changes = [];

        foreach ((string refName, string newId) in after)
        {
            if (!before.TryGetValue(refName, out string? oldId))
            {
                changes.Add(new RefChange(refName, null, newId, RefChangeKind.Created));
            }
            else if (!string.Equals(oldId, newId, StringComparison.Ordinal))
            {
                changes.Add(new RefChange(refName, oldId, newId, RefChangeKind.Updated));
            }
        }

        foreach ((string refName, string oldId) in before)
        {
            if (!after.ContainsKey(refName))
            {
                changes.Add(new RefChange(refName, oldId, null, RefChangeKind.Deleted));
            }
        }

        return [.. changes.OrderBy(change => change.RefName, StringComparer.Ordinal)];
    }
}
