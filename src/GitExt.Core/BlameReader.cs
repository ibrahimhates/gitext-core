using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// The origin of a line in blame (P07-T16).
/// </summary>
public sealed record BlameLine
{
    /// <summary>The commit that last changed the line.</summary>
    public required string ObjectId { get; init; }

    /// <summary>The line number in the current file (1-based).</summary>
    public required int LineNumber { get; init; }

    /// <summary>The line number in that commit.</summary>
    public int OriginalLineNumber { get; init; }

    /// <summary>The line's content.</summary>
    public string Content { get; init; } = string.Empty;

    public string AuthorName { get; init; } = string.Empty;

    public string AuthorEmail { get; init; } = string.Empty;

    public DateTimeOffset AuthorTime { get; init; }

    /// <summary>Commit'in konusu.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// The line's file name in that commit.
    /// </summary>
    /// <remarks>
    /// It differs from the current one across renames; "go to the previous version" uses this.
    /// </remarks>
    public string FileName { get; init; } = string.Empty;

    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;

    /// <summary>
    /// Does this line come from the oldest commit the blame covers?
    /// </summary>
    /// <remarks>
    /// git marks this with <c>boundary</c>; there is no going back further.
    /// </remarks>
    public bool IsBoundary { get; init; }
}

/// <summary>Blame okuma (P07-T16).</summary>
public interface IBlameReader
{
    /// <param name="workingDirectory">The repository's working directory.</param>
    /// <param name="path">The file to blame.</param>
    /// <param name="revision">
    /// Which version to look from; <c>HEAD</c> when <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<BlameLine>> ReadAsync(
        string workingDirectory,
        RepositoryPath path,
        string? revision = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The <c>git blame --porcelain</c> reader (P07-T16).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>MEASURED — <c>--porcelain</c> writes the metadata ONCE PER COMMIT.</b>
/// On the second line coming from the same commit there is only the header line; <c>author</c>,
/// <c>summary</c> and the rest are <b>not repeated</b>. A reader parsing each line independently
/// would show those lines' author as <b>empty</b> — and in the most common case at that (consecutive
/// lines from the same commit).
/// </para>
/// <para>
/// → The metadata is cached by SHA. <c>--line-porcelain</c> would repeat it on every line but it
/// multiplies the output several times over; on large files that is a needless cost.
/// </para>
/// </remarks>
public sealed class BlameReader : IBlameReader
{
    private readonly IGitProcessRunner _runner;

    public BlameReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<BlameLine>> ReadAsync(
        string workingDirectory,
        RepositoryPath path,
        string? revision = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (path.IsEmpty)
        {
            return [];
        }

        List<string> arguments = ["blame", "--porcelain"];

        if (revision is { Length: > 0 } target)
        {
            arguments.Add(target);
        }

        arguments.Add("--");
        arguments.Add(path.Value);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, [.. arguments]),
            cancellationToken).ConfigureAwait(false);

        // A binary file or a path that does not exist: no blame, and not an error either.
        return result.IsSuccess ? Parse(result.GetStandardOutputLossless()) : [];
    }

    /// <summary>The metadata that arrives once per commit.</summary>
    private sealed record CommitInfo
    {
        public string AuthorName { get; set; } = string.Empty;

        public string AuthorEmail { get; set; } = string.Empty;

        public DateTimeOffset AuthorTime { get; set; }

        public string Summary { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public bool IsBoundary { get; set; }
    }

    /// <summary>Parses the <c>--porcelain</c> output.</summary>
    internal static IReadOnlyList<BlameLine> Parse(string output)
    {
        List<BlameLine> lines = [];

        // 🔴 The cache: the metadata arrives ONCE PER COMMIT.
        Dictionary<string, CommitInfo> commits = new(StringComparer.Ordinal);

        string currentSha = string.Empty;
        int finalLine = 0;
        int originalLine = 0;
        CommitInfo? current = null;

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            // A content line starts with a tab — the only thing that tells it apart from the headers.
            if (line[0] == '\t')
            {
                if (current is not null)
                {
                    lines.Add(new BlameLine
                    {
                        ObjectId = currentSha,
                        LineNumber = finalLine,
                        OriginalLineNumber = originalLine,
                        Content = line[1..],
                        AuthorName = current.AuthorName,
                        AuthorEmail = current.AuthorEmail,
                        AuthorTime = current.AuthorTime,
                        Summary = current.Summary,
                        FileName = current.FileName,
                        IsBoundary = current.IsBoundary,
                    });
                }

                continue;
            }

            string[] parts = line.Split(' ');

            // The header: "<sha> <original line> <final line> [<line count>]"
            if (parts.Length >= 3 && IsObjectId(parts[0]))
            {
                currentSha = parts[0];

                originalLine = int.TryParse(parts[1], CultureInfo.InvariantCulture, out int original)
                    ? original
                    : 0;

                finalLine = int.TryParse(parts[2], CultureInfo.InvariantCulture, out int final)
                    ? final
                    : 0;

                if (!commits.TryGetValue(currentSha, out CommitInfo? info))
                {
                    info = new CommitInfo();
                    commits[currentSha] = info;
                }

                current = info;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            // The key/value lines — they only arrive on the commit's FIRST appearance.
            int space = line.IndexOf(' ', StringComparison.Ordinal);
            string key = space < 0 ? line : line[..space];
            string value = space < 0 ? string.Empty : line[(space + 1)..];

            switch (key)
            {
                case "author":
                    current.AuthorName = value;
                    break;
                case "author-mail":
                    current.AuthorEmail = value.Trim('<', '>');
                    break;
                case "author-time":
                    if (long.TryParse(value, CultureInfo.InvariantCulture, out long seconds))
                    {
                        current.AuthorTime = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    }

                    break;
                case "summary":
                    current.Summary = value;
                    break;
                case "filename":
                    current.FileName = value;
                    break;
                case "boundary":
                    current.IsBoundary = true;
                    break;
                default:
                    break;
            }
        }

        return lines;
    }

    private static bool IsObjectId(string value) =>
        value.Length is >= 7 and <= 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
