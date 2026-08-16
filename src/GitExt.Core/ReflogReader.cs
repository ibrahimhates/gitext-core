using System.Globalization;
using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// What caused a reflog entry to be created (P07-T14).
/// </summary>
/// <remarks>
/// git does not provide this as a separate field; it is derived from the <b>first word</b> of the
/// <c>%gs</c> text (<c>commit:</c>, <c>reset:</c>, <c>rebase (finish):</c> …). The text is not
/// localised — git does not translate reflog action names (measured).
/// </remarks>
public enum ReflogAction
{
    /// <summary>An unrecognised or new action.</summary>
    Other,

    Commit,
    Amend,
    Checkout,
    Reset,
    Merge,
    Rebase,
    CherryPick,
    Revert,
    Pull,
    Clone,
    Branch,
    Stash,
}

/// <summary>
/// Tek bir reflog girdisi (P07-T14).
/// </summary>
public sealed record ReflogEntry
{
    /// <summary>The commit the entry points at (full SHA).</summary>
    public required string ObjectId { get; init; }

    /// <summary>The selector — <c>HEAD@{3}</c> or <c>refs/heads/main@{2}</c>.</summary>
    public required string Selector { get; init; }

    /// <summary>The raw action text (<c>%gs</c>), e.g. <c>reset: moving to HEAD~1</c>.</summary>
    public required string Message { get; init; }

    /// <summary>The subject of the commit the entry belongs to (<c>%s</c>).</summary>
    public string Subject { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    public string AuthorName { get; init; } = string.Empty;

    public ReflogAction Action { get; init; }

    /// <summary>
    /// Does this entry point at a commit unreachable from the <b>current</b> HEAD?
    /// </summary>
    /// <remarks>
    /// This is what the reflog browser is actually for: finding the "lost" commit. The value is
    /// filled in by the reader; <see cref="ReflogReader"/> computes it with a separate query.
    /// </remarks>
    public bool IsUnreachable { get; init; }

    /// <summary>The abbreviated SHA — the one shown in the list.</summary>
    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;

    /// <summary>
    /// The command to run in order to return to this entry.
    /// </summary>
    /// <remarks>
    /// ⚠️ The SHA is written, <b>not</b> the selector (<c>HEAD@{3}</c>). The selector is a
    /// <b>sliding</b> reference: as soon as another operation adds an entry to the reflog,
    /// <c>HEAD@{3}</c> points at an entirely different commit. If the user copied the command and
    /// ran it five minutes later they would go back to the wrong place. (The same lesson as
    /// <c>ORIG_HEAD</c> in P06-T07.)
    /// </remarks>
    public string RecoveryCommand => $"git reset --hard {ObjectId}";
}

/// <summary>Reflog okuma (P07-T14).</summary>
public interface IReflogReader
{
    /// <summary>
    /// Reads the reflog entries.
    /// </summary>
    /// <param name="workingDirectory">The repository's working directory.</param>
    /// <param name="reference">
    /// <c>HEAD</c>, a branch name, or <see langword="null"/> for all of them.
    /// </param>
    /// <param name="limit">How many entries to read at most.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<ReflogEntry>> ReadAsync(
        string workingDirectory,
        string? reference = null,
        int limit = 200,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The <c>git reflog</c> reader (P07-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>This class is the phase's insurance policy.</b> Every operation in Phase 07 rewrites
/// history; when the user loses something, this is where they will get it back. That is why the
/// plan says it "must be done early in the phase, not left to the end".
/// </para>
/// <para>
/// 🔴 <b>MEASURED — a TAB separator is not safe.</b> A commit message can contain a tab and
/// <c>%s</c> prints it <b>as is</b>; a parser splitting on tabs sees an extra field and shifts the
/// row. (Curiously <c>%gs</c> turns a tab into a space while <c>%s</c> does not — so "one field is
/// safe" guarantees nothing about the other.) The fields are therefore separated by <b>NUL</b>,
/// which cannot occur in a commit message.
/// </para>
/// <para>
/// ℹ️ <b>MEASURED — <c>git fsck</c> is not needed.</b> A commit "lost" to <c>reset --hard</c> is
/// still in the reflog; there is no need to scan for unreachable objects.
/// </para>
/// </remarks>
public sealed class ReflogReader : IReflogReader
{
    /// <summary>The field separator — NUL.</summary>
    private const char FieldSeparator = '\0';

    /// <summary>The record separator — ASCII Record Separator (0x1e).</summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — a NUL PAIR is NOT SAFE as a record separator.</b> When a field is empty (an
    /// empty commit message, an empty tagger) two NULs end up side by side and cannot be told from
    /// the separator; the record was being split in two. The separator is <c>%x1e</c> (ASCII Record
    /// Separator) — git supports it in <c>log</c>-based commands and it cannot occur inside the
    /// fields.
    /// </remarks>
    private const char RecordSeparator = '\u001e';

    private const string Format = "%x1e%H%x00%gD%x00%gs%x00%s%x00%ct%x00%an";

    private readonly IGitProcessRunner _runner;

    public ReflogReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<ReflogEntry>> ReadAsync(
        string workingDirectory,
        string? reference = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<string> arguments =
        [
            "reflog",
            "show",
            $"--format={Format}",
            $"--max-count={limit.ToString(CultureInfo.InvariantCulture)}",
        ];

        if (reference is { Length: > 0 } target)
        {
            // No `--` separator: `reflog show` takes no path, but a branch name starting with `-`
            // would be taken for a flag. `--all` is a flag in its own right and cannot pass as a ref.
            arguments.Add(target);
        }
        else
        {
            arguments.Add("--all");
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, [.. arguments]),
            cancellationToken).ConfigureAwait(false);

        // A repository with no reflog (no commits yet) returns an error; that means an empty list.
        if (!result.IsSuccess)
        {
            return [];
        }

        IReadOnlyList<ReflogEntry> entries = Parse(result.GetStandardOutputText());

        return await MarkUnreachableAsync(workingDirectory, entries, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Parses the NUL-separated reflog output.</summary>
    internal static IReadOnlyList<ReflogEntry> Parse(string output)
    {
        List<ReflogEntry> entries = [];

        foreach (string record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // git appends a line ending to every record; unless it is trimmed it sticks to the
            // LAST field (the author name).
            string trimmed = record.Trim('\n', '\r');

            if (trimmed.Length == 0)
            {
                continue;
            }

            string[] fields = trimmed.Split(FieldSeparator);

            // If the field count does not match, the line is not ours; rather than invent one, it is skipped.
            if (fields.Length < 6 || fields[0].Length == 0)
            {
                continue;
            }

            entries.Add(new ReflogEntry
            {
                ObjectId = fields[0],
                Selector = fields[1],
                Message = fields[2],
                Subject = fields[3],
                Timestamp = ParseTimestamp(fields[4]),
                AuthorName = fields[5],
                Action = ClassifyAction(fields[2]),
            });
        }

        return entries;
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        long.TryParse(value, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : default;

    /// <summary>
    /// Extracts the action from the <c>%gs</c> text.
    /// </summary>
    /// <remarks>
    /// The text arrives in forms like <c>commit: …</c>, <c>commit (amend): …</c>,
    /// <c>rebase (finish): …</c>. The part up to the first colon is taken, and the parenthesised
    /// suffix inside it is taken into account as well.
    /// </remarks>
    internal static ReflogAction ClassifyAction(string message)
    {
        int colon = message.IndexOf(':', StringComparison.Ordinal);
        ReadOnlySpan<char> head = colon < 0 ? message : message.AsSpan(0, colon);

        // Telling `commit (amend)` from `commit (initial)`: amend rewrites history.
        if (head.Contains("amend", StringComparison.OrdinalIgnoreCase))
        {
            return ReflogAction.Amend;
        }

        ReadOnlySpan<char> verb = head;
        int space = head.IndexOf(' ');

        if (space > 0)
        {
            verb = head[..space];
        }

        return verb switch
        {
            "commit" => ReflogAction.Commit,
            "checkout" => ReflogAction.Checkout,
            "reset" => ReflogAction.Reset,
            "merge" => ReflogAction.Merge,
            "rebase" => ReflogAction.Rebase,
            "cherry-pick" => ReflogAction.CherryPick,
            "revert" => ReflogAction.Revert,
            "pull" => ReflogAction.Pull,
            "clone" => ReflogAction.Clone,
            "branch" => ReflogAction.Branch,
            "stash" => ReflogAction.Stash,
            _ => ReflogAction.Other,
        };
    }

    /// <summary>
    /// Marks which entries are unreachable from the <b>current</b> history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The candidates are given on <b>stdin</b> and filtered with <c>--not --all</c>: git writes
    /// back only the commits unreachable from any ref. Running one
    /// <c>merge-base --is-ancestor</c> per entry would open hundreds of processes.
    /// </para>
    /// <para>
    /// 🔴 <b>The first version used <c>rev-list --all --no-walk=unsorted HEAD</c> and it was
    /// WRONG:</b> <c>--no-walk</c> <b>does not walk</b> the history, it only prints the tips. In a
    /// three-commit repository it returned a single line; the upshot was that <b>every</b> older
    /// reflog entry after the first commit would be marked "lost commit". Caught by measurement,
    /// pinned down by a test.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<ReflogEntry>> MarkUnreachableAsync(
        string workingDirectory,
        IReadOnlyList<ReflogEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return entries;
        }

        string candidates = string.Join(
            '\n',
            entries.Select(entry => entry.ObjectId).Distinct(StringComparer.Ordinal));

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-list", "--no-walk", "--stdin", "--not", "--all"],
                StandardInput = System.Text.Encoding.UTF8.GetBytes(candidates + "\n"),
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // If we cannot determine it, we DO NOT SAY "lost": a false lost-commit warning sends the
            // user chasing a problem that does not exist.
            return entries;
        }

        HashSet<string> unreachable = new(StringComparer.Ordinal);

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            unreachable.Add(line.Trim());
        }

        return [.. entries.Select(entry => entry with
        {
            IsUnreachable = unreachable.Contains(entry.ObjectId),
        })];
    }
}
