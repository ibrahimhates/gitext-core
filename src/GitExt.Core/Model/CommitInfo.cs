namespace GitExt.Core.Model;

/// <summary>
/// The information about a commit shown in the list and in the details panel.
/// </summary>
/// <remarks>
/// The fields are limited to what <c>git log</c> can return in a single pass; making an extra
/// process call per field produces unacceptable cost in large repositories (ADR-0002).
/// </remarks>
public sealed record CommitInfo
{
    /// <summary>Full commit id.</summary>
    public required CommitId Id { get; init; }

    /// <summary>
    /// Parent commits.
    /// </summary>
    /// <remarks>
    /// Empty for a root commit; more than one for a merge. An octopus merge can contain more
    /// than two parents — this list is deliberately unbounded.
    /// </remarks>
    public required IReadOnlyList<CommitId> Parents { get; init; }

    /// <summary>The person who wrote the change.</summary>
    public required Signature Author { get; init; }

    /// <summary>
    /// The person who recorded the commit into the repository.
    /// </summary>
    /// <remarks>
    /// After a rebase, a cherry-pick or applying a patch this differs from the author; the
    /// date differs too. Which one the graph shows is a UI decision.
    /// </remarks>
    public required Signature Committer { get; init; }

    /// <summary>First line of the message.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// The remaining body of the message. Empty for commits that are a subject only.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>
    /// Refs pointing at this commit (branch, tag, <c>HEAD</c>).
    /// </summary>
    public IReadOnlyList<string> Refs { get; init; } = [];

    /// <summary>
    /// The encoding recorded in the commit object; empty when it is UTF-8.
    /// </summary>
    /// <remarks>
    /// Informational only. <see cref="Subject"/> and <see cref="Body"/> always arrive already
    /// converted to UTF-8 — <c>i18n.logOutputEncoding=UTF-8</c> guarantees that.
    /// </remarks>
    public string Encoding { get; init; } = string.Empty;

    /// <summary>Is this a commit with more than one parent?</summary>
    public bool IsMerge => Parents.Count > 1;

    /// <summary>Is this a commit with no parents (the root of history)?</summary>
    public bool IsRoot => Parents.Count == 0;

    /// <summary>
    /// The full message, subject and body joined.
    /// </summary>
    public string FullMessage =>
        string.IsNullOrEmpty(Body) ? Subject : $"{Subject}\n\n{Body}";

    public override string ToString() => $"{Id.ToShortString()} {Subject}";
}
