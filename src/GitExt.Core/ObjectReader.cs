using System.Globalization;
using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Reaches raw Git objects: file contents and tree listings (P02-T11).
/// </summary>
public interface IObjectReader
{
    /// <summary>
    /// Lists the tree at a revision.
    /// </summary>
    /// <param name="workingDirectory">The repository's working directory.</param>
    /// <param name="revision">The revision (branch, tag, SHA, <c>HEAD</c>).</param>
    /// <param name="path">A subdirectory; the root when <see langword="null"/>.</param>
    /// <param name="recursive">Should it descend into subdirectories?</param>
    /// <param name="includeSize">Should blob sizes be included too (<c>--long</c>)?</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<TreeEntry>> ReadTreeAsync(
        string workingDirectory,
        string revision,
        RepositoryPath? path = null,
        bool recursive = false,
        bool includeSize = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds out the type and size of objects without reading their content.
    /// </summary>
    /// <remarks>
    /// For checking the size of a very large file before pulling it into memory.
    /// </remarks>
    /// <param name="workingDirectory">The repository's working directory.</param>
    /// <param name="revisions">The objects to query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<GitObjectInfo>> GetInfoAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads several blobs in <b>a single process call</b>.
    /// </summary>
    /// <remarks>
    /// <c>cat-file --batch</c> accepts several objects on stdin. Starting a separate process per file
    /// (the N+1 pattern) is ADR-0002's known weakness; batch reading is the primary answer to it.
    /// </remarks>
    /// <param name="workingDirectory">The repository's working directory.</param>
    /// <param name="revisions">The objects to read, e.g. <c>HEAD:src/a.txt</c>.</param>
    /// <param name="maxBytes">
    /// The maximum bytes to read per object. Content beyond it is truncated and
    /// <see cref="BlobContent.IsTruncated"/> is set.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<IReadOnlyList<BlobContent>> ReadBlobsAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        long maxBytes = ObjectReader.DefaultMaxBlobBytes,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IObjectReader"/>
public sealed class ObjectReader : IObjectReader
{
    /// <summary>
    /// The default per-object read limit: 10 MB.
    /// </summary>
    /// <remarks>
    /// Without a limit, a single 200 MB file locks up the UI. The user can re-read with a higher
    /// value if they want to.
    /// </remarks>
    public const long DefaultMaxBlobBytes = 10L * 1024 * 1024;

    /// <summary>
    /// The number of bytes scanned for binary detection — the value <c>git</c> uses.
    /// </summary>
    private const int BinaryDetectionWindow = 8000;

    private readonly IGitProcessRunner _runner;

    public ObjectReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<TreeEntry>> ReadTreeAsync(
        string workingDirectory,
        string revision,
        RepositoryPath? path = null,
        bool recursive = false,
        bool includeSize = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        // ls-tree -z IS SUPPORTED (unlike for-each-ref — measured).
        List<string> arguments = ["ls-tree", "-z"];

        if (recursive)
        {
            arguments.Add("-r");
        }

        if (includeSize)
        {
            arguments.Add("--long");
        }

        arguments.Add(revision);

        if (path is { } subPath)
        {
            arguments.Add("--");
            // Without the trailing slash git returns the directory itself as a single entry; it is
            // needed to list its contents.
            arguments.Add(subPath.Value + "/");
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand { WorkingDirectory = workingDirectory, Arguments = arguments },
            cancellationToken).ConfigureAwait(false);

        List<TreeEntry> entries = [];

        foreach (string record in result.SplitStandardOutputAtNul())
        {
            if (ParseTreeEntry(record) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// <c>&lt;mode&gt; &lt;type&gt; &lt;sha&gt;[ &lt;size&gt;]&lt;TAB&gt;&lt;path&gt;</c>
    /// </summary>
    /// <remarks>
    /// The separator between the metadata and the path is a <b>TAB</b>, not a space — a path can
    /// contain spaces. Because a path can contain a TAB too, it is split only at the <b>first</b> TAB.
    /// </remarks>
    internal static TreeEntry? ParseTreeEntry(string record)
    {
        int tab = record.IndexOf('\t', StringComparison.Ordinal);

        if (tab < 0 || !RepositoryPath.TryParse(record[(tab + 1)..], out RepositoryPath path))
        {
            return null;
        }

        string[] metadata = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (metadata.Length < 3 || !CommitId.TryParse(metadata[2], out CommitId id))
        {
            return null;
        }

        // When --long was passed the fourth field is the size; for trees it comes back as "-".
        long? size = metadata.Length > 3
                     && long.TryParse(metadata[3], CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;

        return new TreeEntry
        {
            Path = path,
            Mode = metadata[0],
            Type = ParseObjectType(metadata[1]),
            Id = id,
            Size = size,
        };
    }

    public async Task<IReadOnlyList<GitObjectInfo>> GetInfoAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(revisions);

        if (revisions.Count == 0)
        {
            return [];
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["cat-file", "--batch-check"],
                StandardInput = BuildBatchInput(revisions),
            },
            cancellationToken).ConfigureAwait(false);

        List<GitObjectInfo> infos = [];

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            infos.Add(ParseBatchHeader(line) is { } header
                ? new GitObjectInfo { Id = header.Id, Type = header.Type, Size = header.Size }
                : new GitObjectInfo { Id = default, Type = GitObjectType.Missing, Size = 0 });
        }

        return infos;
    }

    public async Task<IReadOnlyList<BlobContent>> ReadBlobsAsync(
        string workingDirectory,
        IReadOnlyList<string> revisions,
        long maxBytes = DefaultMaxBlobBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        if (revisions.Count == 0)
        {
            return [];
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["cat-file", "--batch"],
                StandardInput = BuildBatchInput(revisions),
            },
            cancellationToken).ConfigureAwait(false);

        return ParseBatchOutput(result.StandardOutput, maxBytes);
    }

    private static ReadOnlyMemory<byte> BuildBatchInput(IReadOnlyList<string> revisions)
    {
        StringBuilder builder = new();

        foreach (string revision in revisions)
        {
            builder.Append(revision).Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Parses the <c>cat-file --batch</c> output.
    /// </summary>
    /// <remarks>
    /// The format (measured): <c>&lt;sha&gt; &lt;type&gt; &lt;size&gt;\n&lt;content&gt;\n</c>.
    /// A missing object: <c>&lt;input&gt; missing\n</c> — with no content.
    /// <para>
    /// The content <b>may be binary</b>, so it is handled at byte level: after the header exactly
    /// <c>size</c> bytes are read, then a closing line ending is skipped. Converting to text and
    /// splitting would corrupt the content irreversibly.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<BlobContent> ParseBatchOutput(byte[] output, long maxBytes)
    {
        List<BlobContent> blobs = [];
        int offset = 0;

        while (offset < output.Length)
        {
            int newline = Array.IndexOf(output, (byte)'\n', offset);

            if (newline < 0)
            {
                break;
            }

            string header = Encoding.UTF8.GetString(output, offset, newline - offset);
            offset = newline + 1;

            if (ParseBatchHeader(header) is not { } parsed)
            {
                // A "…missing" line: there is no content body, move on to the next header.
                blobs.Add(new BlobContent
                {
                    Id = default,
                    Size = 0,
                    Content = [],
                    IsBinary = false,
                });
                continue;
            }

            int available = (int)Math.Min(parsed.Size, output.Length - offset);
            int take = (int)Math.Min(available, maxBytes);

            byte[] content = new byte[take];
            Array.Copy(output, offset, content, 0, take);

            blobs.Add(new BlobContent
            {
                Id = parsed.Id,
                Size = parsed.Size,
                Content = content,
                IsBinary = LooksBinary(content),
                IsTruncated = take < parsed.Size,
            });

            // Skip the whole content, then the closing line ending git appends as well.
            offset += available;

            if (offset < output.Length && output[offset] == (byte)'\n')
            {
                offset++;
            }
        }

        return blobs;
    }

    /// <summary>
    /// Parses the <c>&lt;sha&gt; &lt;type&gt; &lt;size&gt;</c> header.
    /// </summary>
    /// <returns><see langword="null"/> when the line reports <c>missing</c>.</returns>
    private static (CommitId Id, GitObjectType Type, long Size)? ParseBatchHeader(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3
            || !CommitId.TryParse(parts[0], out CommitId id)
            || !long.TryParse(parts[2], CultureInfo.InvariantCulture, out long size))
        {
            return null;
        }

        return (id, ParseObjectType(parts[1]), size);
    }

    /// <summary>
    /// The same heuristic as <c>git</c>: binary when there is a NUL in the first 8000 bytes.
    /// </summary>
    private static bool LooksBinary(ReadOnlySpan<byte> content) =>
        content[..Math.Min(content.Length, BinaryDetectionWindow)].Contains((byte)0);

    private static GitObjectType ParseObjectType(string value) => value switch
    {
        "blob" => GitObjectType.Blob,
        "tree" => GitObjectType.Tree,
        "commit" => GitObjectType.Commit,
        "tag" => GitObjectType.Tag,
        _ => GitObjectType.Missing,
    };
}
