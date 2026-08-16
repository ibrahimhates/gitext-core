using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

// ===================================================== P07-T17 file history

/// <summary>A single entry in a file's history (P07-T17).</summary>
public sealed record FileHistoryEntry
{
    public required string ObjectId { get; init; }

    public required string Subject { get; init; }

    public string AuthorName { get; init; } = string.Empty;

    public DateTimeOffset AuthorTime { get; init; }

    /// <summary>
    /// The file's name at that commit.
    /// </summary>
    /// <remarks>
    /// This changes across a rename while tracking; kept so the screen can show "this used
    /// to be named X".
    /// </remarks>
    public string Path { get; init; } = string.Empty;

    /// <summary>Was the file renamed at this commit?</summary>
    public bool IsRename { get; init; }

    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;
}

/// <summary>Reading file history (P07-T17).</summary>
public interface IFileHistoryReader
{
    Task<IReadOnlyList<FileHistoryEntry>> ReadAsync(
        string workingDirectory,
        RepositoryPath path,
        int limit = 200,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git log --follow</c> reader (P07-T17).
/// </summary>
/// <remarks>
/// <b>MEASURED — <c>--follow</c> really does make a difference.</b> For a once-renamed
/// file, <c>--follow</c> showed 3 commits, without it only <b>1</b> commit: the history
/// before the rename disappears entirely. The user would think "is this really all the
/// history this file has".
/// </remarks>
public sealed class FileHistoryReader : IFileHistoryReader
{
    /// <summary>Record separator is <b>at the start</b>.</summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — if the separator is at the end, <c>--name-status</c> lines land in
    /// the wrong record.</b> git writes those lines <b>after</b> the format output; with a
    /// trailing separator, each split chunk started with the <b>previous</b> commit's status
    /// lines. Result: a rename would be attributed to the next commit. With the separator
    /// moved to the front, each chunk carries its own status lines.
    /// </remarks>
    private const string Format = "%x1e%H%x00%s%x00%an%x00%at";

    private readonly IGitProcessRunner _runner;

    public FileHistoryReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<FileHistoryEntry>> ReadAsync(
        string workingDirectory,
        RepositoryPath path,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (path.IsEmpty)
        {
            return [];
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory,
                "log",
                "--follow",
                "--name-status",
                $"--format={Format}",
                $"--max-count={limit.ToString(CultureInfo.InvariantCulture)}",
                "--",
                path.Value),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText(), path.Value) : [];
    }

    /// <remarks>
    /// Each record: <c>&lt;sha&gt;\0&lt;subject&gt;\0&lt;author&gt;\0&lt;time&gt;</c>, followed
    /// by <c>--name-status</c> lines — either <c>R100&lt;TAB&gt;old&lt;TAB&gt;new</c> or
    /// <c>M&lt;TAB&gt;path</c>. Renames are read from here.
    /// </remarks>
    internal static IReadOnlyList<FileHistoryEntry> Parse(string output, string currentPath)
    {
        List<FileHistoryEntry> entries = [];

        foreach (string record in output.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Split('\0');

            if (fields.Length < 4 || fields[0].Length == 0)
            {
                continue;
            }

            // Last field: the timestamp + the status lines that follow it.
            string[] tail = fields[3].Split('\n', StringSplitOptions.RemoveEmptyEntries);

            string path = currentPath;
            bool rename = false;

            foreach (string status in tail.Skip(1))
            {
                string[] columns = status.Split('\t');

                if (columns.Length >= 3 && columns[0].StartsWith('R'))
                {
                    // On a rename the OLD name is the interesting one: "this file used to be that".
                    rename = true;
                    path = columns[1];
                    break;
                }

                if (columns.Length >= 2)
                {
                    path = columns[1];
                }
            }

            entries.Add(new FileHistoryEntry
            {
                ObjectId = fields[0],
                Subject = fields[1],
                AuthorName = fields[2],
                AuthorTime =
                    long.TryParse(tail.FirstOrDefault(), CultureInfo.InvariantCulture, out long seconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                        : default,
                Path = path,
                IsRename = rename,
            });
        }

        return entries;
    }
}

// ============================================================ P07-T18 tag

/// <summary>A tag (P07-T18).</summary>
public sealed record GitTag
{
    public required string Name { get; init; }

    /// <summary>The commit the tag points to.</summary>
    public required string ObjectId { get; init; }

    /// <summary>
    /// Is this an annotated tag?
    /// </summary>
    /// <remarks>
    /// MEASURED: the distinction is made via <c>%(objecttype)</c> — <c>tag</c> for
    /// annotated, <c>commit</c> for lightweight. For annotated tags,
    /// <c>%(*objectname)</c> gives the actual commit; <c>%(objectname)</c> is the
    /// <b>tag object's</b> own SHA, not the commit's.
    /// </remarks>
    public bool IsAnnotated { get; init; }

    public string Message { get; init; } = string.Empty;

    public string TaggerName { get; init; } = string.Empty;

    public DateTimeOffset? TaggedAt { get; init; }
}

/// <summary>Tag creation options (P07-T18).</summary>
public sealed record TagOptions
{
    public required string Name { get; init; }

    /// <summary>Commit to tag; <see langword="null"/> means <c>HEAD</c>.</summary>
    public string? Target { get; init; }

    /// <summary>Annotation text; if given, the tag becomes <b>annotated</b>.</summary>
    public string? Message { get; init; }

    /// <summary><c>--sign</c>: sign with GPG/SSH.</summary>
    public bool Sign { get; init; }

    /// <summary><c>--force</c>: move a tag with the same name.</summary>
    public bool Force { get; init; }
}

/// <summary>Tag operations (P07-T18).</summary>
public interface ITagWriter
{
    Task<IReadOnlyList<GitTag>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        string workingDirectory,
        TagOptions options,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary><c>git tag</c> wrapper (P07-T18).</summary>
public sealed class TagWriter : ITagWriter
{
    /// <remarks>
    /// 🔴 <b><c>for-each-ref</c> does NOT SUPPORT <c>%x1e</c></b> — measured, the escape
    /// sequence was printed literally as <c>%x1e</c> (it works in <c>log</c>-based
    /// commands). Records here are separated by <b>newline</b> instead; that's safe
    /// because the tag name, object name, and <c>contents:subject</c> are all a single line.
    /// <para>
    /// Fields are still NUL-separated: a lightweight tag's <c>*objectname</c>/<c>taggername</c>
    /// fields come back <b>empty</b>, and using a double-NUL would have produced a fake
    /// record boundary.
    /// </para>
    /// </remarks>
    private const string Format =
        "%(refname:short)%00%(objecttype)%00%(objectname)%00%(*objectname)%00"
        + "%(contents:subject)%00%(taggerdate:unix)%00%(taggername)";

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public TagWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<IReadOnlyList<GitTag>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "for-each-ref", $"--format={Format}", "refs/tags"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    internal static IReadOnlyList<GitTag> Parse(string output)
    {
        List<GitTag> tags = [];

        foreach (string record in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.TrimEnd('\r').Split('\0');

            if (fields.Length < 7 || fields[0].Length == 0)
            {
                continue;
            }

            bool annotated = string.Equals(fields[1], "tag", StringComparison.Ordinal);

            tags.Add(new GitTag
            {
                Name = fields[0],
                IsAnnotated = annotated,

                // For annotated tags `%(objectname)` is the TAG OBJECT'S SHA; the commit
                // is in `%(*objectname)`. Mixing them up would mean clicking the tag
                // navigates to a commit that doesn't exist.
                ObjectId = annotated && fields[3].Length > 0 ? fields[3] : fields[2],
                Message = fields[4],
                TaggedAt = long.TryParse(fields[5], CultureInfo.InvariantCulture, out long seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : null,
                TaggerName = fields[6],
            });
        }

        return tags;
    }

    public Task CreateAsync(
        string workingDirectory,
        TagOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);

        List<string> arguments = ["tag"];

        if (options.Force)
        {
            arguments.Add("--force");
        }

        if (options.Sign)
        {
            arguments.Add("--sign");
        }

        if (options.Message is { Length: > 0 } message)
        {
            arguments.Add("--annotate");
            arguments.Add("-m");
            arguments.Add(message);
        }

        arguments.Add(options.Name);

        if (options.Target is { Length: > 0 } target)
        {
            arguments.Add(target);
        }

        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }

    public Task DeleteAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _writer.RunAsync(workingDirectory, ["tag", "--delete", name], cancellationToken);
    }
}

// ======================================================== P07-T20 worktree

/// <summary>A linked working tree (P07-T20).</summary>
public sealed record WorkTree
{
    public required string Path { get; init; }

    public string ObjectId { get; init; } = string.Empty;

    /// <summary>The branch checked out on it; <see langword="null"/> if a detached <c>HEAD</c>.</summary>
    public string? BranchName { get; init; }

    /// <summary>Is this the main working tree? (First in the list.)</summary>
    public bool IsMain { get; init; }

    public bool IsDetached => BranchName is null;

    /// <summary>Is it locked? A locked worktree can't be removed.</summary>
    public bool IsLocked { get; init; }

    /// <summary>Is its directory gone?</summary>
    public bool IsPrunable { get; init; }
}

/// <summary>Worktree operations (P07-T20).</summary>
public interface IWorkTreeReader
{
    Task<IReadOnlyList<WorkTree>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        string workingDirectory,
        string path,
        string? branch,
        bool createBranch,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string workingDirectory,
        string path,
        bool force,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git worktree</c> wrapper (P07-T20).
/// </summary>
/// <remarks>
/// <c>--porcelain</c> output is <b>blocks separated by a blank line</b>: each block starts
/// with <c>worktree &lt;path&gt;</c>, followed by keys like <c>HEAD</c>, <c>branch</c>,
/// <c>detached</c>, <c>locked</c>, <c>prunable</c>.
/// </remarks>
public sealed class WorkTreeReader : IWorkTreeReader
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public WorkTreeReader(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<IReadOnlyList<WorkTree>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "worktree", "list", "--porcelain"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    internal static IReadOnlyList<WorkTree> Parse(string output)
    {
        List<WorkTree> trees = [];

        foreach (string block in output.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string path = string.Empty;
            string objectId = string.Empty;
            string? branch = null;
            bool locked = false;
            bool prunable = false;

            foreach (string raw in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.TrimEnd('\r');
                int space = line.IndexOf(' ', StringComparison.Ordinal);
                string key = space < 0 ? line : line[..space];
                string value = space < 0 ? string.Empty : line[(space + 1)..];

                switch (key)
                {
                    case "worktree":
                        path = value;
                        break;
                    case "HEAD":
                        objectId = value;
                        break;
                    case "branch":
                        // `refs/heads/main` → `main`
                        branch = value.StartsWith("refs/heads/", StringComparison.Ordinal)
                            ? value["refs/heads/".Length..]
                            : value;
                        break;
                    case "locked":
                        locked = true;
                        break;
                    case "prunable":
                        prunable = true;
                        break;
                    default:
                        break;
                }
            }

            if (path.Length == 0)
            {
                continue;
            }

            trees.Add(new WorkTree
            {
                Path = path,
                ObjectId = objectId,
                BranchName = branch,

                // git always writes the main working tree first.
                IsMain = trees.Count == 0,
                IsLocked = locked,
                IsPrunable = prunable,
            });
        }

        return trees;
    }

    public Task AddAsync(
        string workingDirectory,
        string path,
        string? branch,
        bool createBranch,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        List<string> arguments = ["worktree", "add"];

        if (createBranch && branch is { Length: > 0 } created)
        {
            arguments.Add("-b");
            arguments.Add(created);
        }

        arguments.Add(path);

        if (!createBranch && branch is { Length: > 0 } existing)
        {
            arguments.Add(existing);
        }

        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }

    /// <remarks>
    /// ⚠️ <c>--force</c> only when the user explicitly asks for it: force-removing a dirty
    /// worktree deletes uncommitted work.
    /// </remarks>
    public Task RemoveAsync(
        string workingDirectory,
        string path,
        bool force,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        List<string> arguments = ["worktree", "remove"];

        if (force)
        {
            arguments.Add("--force");
        }

        arguments.Add(path);
        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }
}

// ======================================================= P07-T21 arama

/// <summary>The criteria of a commit search (P07-T21).</summary>
public sealed record CommitSearchQuery
{
    /// <summary><c>--grep</c>: search in the commit message.</summary>
    public string? Message { get; init; }

    /// <summary><c>--author</c>.</summary>
    public string? Author { get; init; }

    /// <summary>
    /// <c>-S</c> (pickaxe): the commits where <b>the number of occurrences</b> of this text changed.
    /// </summary>
    /// <remarks>
    /// The difference between <c>-S</c> and <c>-G</c> is subtle but important: <c>-S</c> asks "did the
    /// number of occurrences of this string change" (that is, was it added or removed), while <c>-G</c>
    /// asks "does the diff itself match this regular expression". A line being moved does not show up
    /// under <c>-S</c> but does under <c>-G</c>.
    /// </remarks>
    public string? ContentAdded { get; init; }

    /// <summary><c>-G</c>: apply a regular expression to the diff text.</summary>
    public string? ContentPattern { get; init; }

    /// <summary>Limit the search to these paths.</summary>
    public IReadOnlyList<RepositoryPath> Paths { get; init; } = [];

    /// <summary>Search case-insensitively.</summary>
    public bool IgnoreCase { get; init; } = true;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Message)
        && string.IsNullOrWhiteSpace(Author)
        && string.IsNullOrWhiteSpace(ContentAdded)
        && string.IsNullOrWhiteSpace(ContentPattern);
}

/// <summary>A match found in file content (P07-T21).</summary>
public sealed record ContentMatch
{
    public required string Path { get; init; }

    public required int LineNumber { get; init; }

    public required string Line { get; init; }
}

/// <summary>Arama (P07-T21).</summary>
public interface ISearchReader
{
    /// <summary>Commit'lerde arar.</summary>
    Task<IReadOnlyList<string>> SearchCommitsAsync(
        string workingDirectory,
        CommitSearchQuery query,
        int limit = 500,
        CancellationToken cancellationToken = default);

    /// <summary>Searches the file contents in the working tree (<c>git grep</c>).</summary>
    Task<IReadOnlyList<ContentMatch>> SearchContentAsync(
        string workingDirectory,
        string pattern,
        bool ignoreCase = true,
        CancellationToken cancellationToken = default);
}

/// <summary>Commit and content search (P07-T21).</summary>
public sealed class SearchReader : ISearchReader
{
    private readonly IGitProcessRunner _runner;

    public SearchReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<string>> SearchCommitsAsync(
        string workingDirectory,
        CommitSearchQuery query,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (query.IsEmpty)
        {
            // An empty query would return the whole history; saying "no search was made" is more honest.
            return [];
        }

        List<string> arguments =
        [
            "log",
            "--format=%H",
            $"--max-count={limit.ToString(CultureInfo.InvariantCulture)}",
        ];

        if (query.IgnoreCase)
        {
            arguments.Add("--regexp-ignore-case");
        }

        if (query.Message is { Length: > 0 } message)
        {
            arguments.Add($"--grep={message}");
        }

        if (query.Author is { Length: > 0 } author)
        {
            arguments.Add($"--author={author}");
        }

        if (query.ContentAdded is { Length: > 0 } added)
        {
            arguments.Add($"-S{added}");
        }

        if (query.ContentPattern is { Length: > 0 } pattern)
        {
            arguments.Add($"-G{pattern}");
        }

        if (query.Paths.Count > 0)
        {
            arguments.Add("--");
            arguments.AddRange(query.Paths.Select(path => path.Value));
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, [.. arguments]),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? [.. result.GetStandardOutputText().Split('\n', StringSplitOptions.RemoveEmptyEntries)]
            : [];
    }

    /// <remarks>
    /// With <c>-z</c> the fields are NUL-separated: a path can contain spaces and colons, and so can the
    /// line content. Without <c>-z</c>, parsing <c>path:line:content</c> would silently shift on a path
    /// containing a colon.
    /// </remarks>
    public async Task<IReadOnlyList<ContentMatch>> SearchContentAsync(
        string workingDirectory,
        string pattern,
        bool ignoreCase = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        List<string> arguments = ["grep", "--line-number", "--no-color", "-z"];

        if (ignoreCase)
        {
            arguments.Add("--ignore-case");
        }

        arguments.Add("-e");
        arguments.Add(pattern);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = arguments,

                // With no match, `git grep` gives exit code 1; that is not an error.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return Parse(result.GetStandardOutputLossless());
    }

    /// <summary>Parses the <c>git grep -z --line-number</c> output.</summary>
    internal static IReadOnlyList<ContentMatch> Parse(string output)
    {
        List<ContentMatch> matches = [];

        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // `<path>\0<line number>\0<content>` — the content cannot contain a NUL.
            string[] fields = line.Split('\0', 3);

            if (fields.Length < 3
                || !int.TryParse(fields[1], CultureInfo.InvariantCulture, out int number))
            {
                continue;
            }

            matches.Add(new ContentMatch
            {
                Path = fields[0],
                LineNumber = number,
                Line = fields[2],
            });
        }

        return matches;
    }
}
