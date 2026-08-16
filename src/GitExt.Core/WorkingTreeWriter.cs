using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// The scope of reverting changes (P05-T08).
/// </summary>
public enum DiscardScope
{
    /// <summary>
    /// Only unstaged changes are discarded; the content in the index is preserved.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED:</b> a plain <c>git restore</c> restores the working tree <b>from the
    /// index</b>, not from HEAD. So a staged change survives. This is the behavior most
    /// users expect from the "revert change" button.
    /// </remarks>
    UnstagedOnly,

    /// <summary>Both staged and unstaged changes are discarded (reverts to HEAD).</summary>
    All,
}

/// <summary>
/// The scope of <c>git clean</c> (P05-T08).
/// </summary>
public sealed record CleanOptions
{
    public static CleanOptions Default { get; } = new();

    /// <summary>Whether untracked <b>directories</b> should also be deleted (<c>-d</c>).</summary>
    /// <remarks>
    /// <b>MEASURED:</b> without <c>-d</c> an untracked directory is <b>not deleted at
    /// all</b>, and this is not reported as an error either.
    /// </remarks>
    public bool IncludeDirectories { get; init; } = true;

    /// <summary>Whether ignored files should also be deleted (<c>-x</c>).</summary>
    /// <remarks>
    /// ⚠️ Dangerous: alongside build output, <b>non-reproducible</b> files like
    /// <c>.env</c> are also typically ignored.
    /// </remarks>
    public bool IncludeIgnored { get; init; }

    /// <summary>Only ignored files should be deleted (<c>-X</c>).</summary>
    public bool OnlyIgnored { get; init; }

    /// <summary>Whether nested git repositories should also be deleted (<c>-ff</c>).</summary>
    /// <remarks>
    /// <b>MEASURED:</b> with a single <c>-f</c> a nested repository (a cloned
    /// subdirectory) is <b>silently skipped</b> — it never appears in the output. The
    /// user thinks it was "cleaned", but the directory keeps sitting there.
    /// </remarks>
    public bool IncludeNestedRepositories { get; init; }
}

/// <summary>
/// A backup of the discarded content taken into the object database (P05-T08).
/// </summary>
/// <remarks>
/// <b>MEASURED:</b> the blob written with <c>git hash-object -w</c> can be read back
/// after the discard operation with <c>git cat-file -p &lt;blob&gt;</c>.
/// <para>
/// ⚠️ <b>Not a guarantee.</b> No ref points to this object; <c>git gc --prune=now</c>
/// deletes it <b>immediately</b> (measured). In contrast, <b>plain <c>git gc</c> does
/// not delete it</b> (measured in P05-T15): dangling objects are preserved for the
/// default <c>gc.pruneExpire=2.weeks</c>. So the backup is a real recovery path, but
/// not permanent — the destructive operation still requires <b>explicit
/// confirmation</b>.
/// </para>
/// </remarks>
public sealed record DiscardBackup
{
    public required RepositoryPath Path { get; init; }

    /// <summary>The blob id of the discarded content.</summary>
    public required string BlobId { get; init; }
}

/// <summary>
/// The result of an attempt to add an entry to <c>.gitignore</c> (P05-T08).
/// </summary>
public enum GitIgnoreOutcome
{
    /// <summary>The pattern was added.</summary>
    Added,

    /// <summary>The path was already ignored; the file was not modified.</summary>
    AlreadyIgnored,

    /// <summary>
    /// The path is <b>tracked</b>; <c>.gitignore</c> has no effect on this file.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED:</b> adding a tracked file to <c>.gitignore</c> <b>does
    /// nothing</b> — <c>git status</c> keeps showing the file, and even
    /// <c>check-ignore</c> reports no match. Silently writing the file and saying
    /// "added" would promise the user a result that does not exist.
    /// <see cref="IStagingWriter.UntrackAsync"/> is required first.
    /// </remarks>
    PathIsTracked,
}

/// <summary>
/// <b>Destructive</b> operations on files in the working tree (P05-T08).
/// </summary>
/// <remarks>
/// Every operation here can delete the user's <b>not-yet-saved</b> work. Per CLAUDE.md
/// § 8 they all require explicit confirmation, and confirmation is enforced as a
/// <b>parameter</b> (the same pattern as <c>GitLock.Remove</c> in P05-T02): leaving the
/// rule in a comment does not stop someone from calling it without confirmation later.
/// </remarks>
public interface IWorkingTreeWriter
{
    /// <summary>
    /// Reverts changes at the given paths.
    /// </summary>
    /// <returns>Backups of the discarded content; an empty list indicates there was no file to back up.</returns>
    Task<IReadOnlyList<DiscardBackup>> DiscardChangesAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        DiscardScope scope,
        bool userConfirmed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Deletes</b> untracked files.
    /// </summary>
    /// <returns>
    /// Backups of the deleted content.
    /// </returns>
    /// <remarks>
    /// <b>⚠️ MEASURED (P05-T15):</b> a file deleted with <c>git clean</c> leaves <b>no
    /// trace</b> in the object database — even <c>git fsck --lost-found</c> does not
    /// find it. That is why the content is backed up with <c>hash-object -w</c> before
    /// deletion; untracked files are typically <b>new source files not yet
    /// committed</b>, and their loss cannot be compensated for.
    /// </remarks>
    Task<IReadOnlyList<DiscardBackup>> DeleteUntrackedAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        bool userConfirmed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans the <b>entire</b> working tree (<c>git clean</c>).
    /// </summary>
    /// <remarks>
    /// Files to be deleted must be listed beforehand with <see cref="IStatusReader"/>.
    /// The output of <c>git clean --dry-run</c> is <b>not parseable</b>: it is
    /// human-readable (<c>Would remove …</c>), does not support <c>-z</c>, and quotes
    /// names with special characters (measured).
    /// </remarks>
    Task CleanAsync(
        string workingDirectory,
        CleanOptions options,
        bool userConfirmed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverts the changes on a file's <b>selected lines</b> (P05-T15).
    /// </summary>
    /// <returns>Backups of the discarded content.</returns>
    /// <remarks>
    /// <para>
    /// <b>MEASURED:</b> <c>git apply --reverse</c> (i.e. WITHOUT <c>--cached</c>)
    /// applies the patch only to the <b>working tree</b>; it does not touch the index.
    /// If the file has a staged version, it stays as is — this is git's own behavior,
    /// and it is also what is expected from a "revert these lines" command.
    /// </para>
    /// <para>
    /// A partial revert is destructive too: the file's <b>entire</b> content is backed
    /// up beforehand, because the revert has to restore the file to its state before
    /// the patch.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<DiscardBackup>> DiscardPartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        bool userConfirmed,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a backup back to the working tree (P05-T15).
    /// </summary>
    /// <returns>The backups that were actually written back.</returns>
    /// <remarks>
    /// <para>
    /// Taking a backup alone is not a safety net: giving the user a blob id and
    /// expecting them to type <c>git cat-file</c> is useless in a moment of panic.
    /// Writing it back must be <b>an operation the application provides</b>.
    /// </para>
    /// <para>
    /// The content is written as <b>raw bytes</b>. Since the backup was taken with
    /// <c>--no-filters</c>, the blob is an exact copy of the file's on-disk state; it
    /// must not be transformed when writing it back either.
    /// </para>
    /// <para>
    /// If the object no longer exists (<c>gc --prune=now</c>) that backup is
    /// <b>silently skipped</b>: partial recovery is better than no recovery at all.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Backs up the on-disk state of the given paths into the object database (P06-T02).
    /// </summary>
    /// <returns>Backups of the paths found on disk; missing ones are skipped.</returns>
    /// <remarks>
    /// Called <b>before</b> a destructive operation. Exposed as a separate operation
    /// because switching branches (<c>switch --discard-changes</c>) needs the same
    /// safety net too, and writing the <c>--no-filters</c> trap a second time meant it
    /// could be forgotten a second time.
    /// </remarks>
    Task<IReadOnlyList<DiscardBackup>> BackupPathsAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscardBackup>> RestoreBackupsAsync(
        string workingDirectory,
        IReadOnlyList<DiscardBackup> backups,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a pattern to the repository's root <c>.gitignore</c> file.
    /// </summary>
    Task<GitIgnoreOutcome> AddToGitIgnoreAsync(
        string workingDirectory,
        RepositoryPath path,
        string pattern,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IWorkingTreeWriter"/>
public sealed class WorkingTreeWriter : IWorkingTreeWriter
{
    /// <summary>
    /// The number of paths placed into a single <c>hash-object</c> call.
    /// </summary>
    /// <remarks>
    /// The paths are given as <b>arguments</b>, not via stdin: <c>--stdin-paths</c>
    /// separates paths by newline, and a file name can contain a newline. The argument
    /// list is chunked because it is not unbounded. Measured: 500 files in a single
    /// call take <b>14 ms</b>.
    /// </remarks>
    private const int BackupBatchSize = 500;

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public WorkingTreeWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<IReadOnlyList<DiscardBackup>> DiscardChangesAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        DiscardScope scope,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(paths);

        RequireConfirmation(
            userConfirmed,
            "Reverting the changes deletes uncommitted content, and there is no way back through "
            + "the reflog; the operation can only be performed with the user's explicit consent.");

        if (paths.Count == 0)
        {
            // ⚠️ With an empty list, `git restore --` would revert the ENTIRE
            // repository (the same protection was put in place for `git add -A --`
            // in P05-T03).
            return [];
        }

        IReadOnlyList<DiscardBackup> backups =
            await BackupAsync(workingDirectory, paths, cancellationToken).ConfigureAwait(false);

        List<string> arguments = ["restore"];

        if (scope == DiscardScope.All)
        {
            // `--source=HEAD` is required: `--staged` alone also takes HEAD as the
            // source, but writing the intent explicitly closes off the risk of
            // forgetting to add `--source` later. ⚠️ Without a HEAD, git fails with
            // `could not resolve 'HEAD'` (measured) — "revert everything" is undefined
            // in a repository with no commits yet.
            arguments.AddRange(["--source=HEAD", "--staged"]);
        }

        arguments.Add("--worktree");
        arguments.Add("--");
        arguments.AddRange(paths.Select(path => path.Value));

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);

        return backups;
    }

    public async Task<IReadOnlyList<DiscardBackup>> DeleteUntrackedAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(paths);

        RequireConfirmation(
            userConfirmed,
            "Deleting untracked files cannot be undone; there is no copy of these files "
            + "does not exist in git. The operation can only be performed with the user's explicit consent.");

        if (paths.Count == 0)
        {
            // ⚠️ Without paths, `git clean -f` deletes the ENTIRE working tree.
            return [];
        }

        // 🔴 Added in P05-T15. MEASURED and it changed the design: a file deleted with
        // `git clean` that was untracked leaves **no trace at all** in the object
        // database (`fsck --lost-found` does not find it either) — meaning this was
        // the repository's only truly irreversible operation. Yet untracked files are
        // typically **new source files not yet committed**: in this very repository,
        // the output of `git clean -dn` listed files that were being written at that
        // moment. Backing up is cheap (500 files = 110 ms); the loss cannot be
        // compensated for.
        IReadOnlyList<DiscardBackup> backups =
            await BackupAsync(workingDirectory, paths, cancellationToken).ConfigureAwait(false);

        // `-x`: 🔴 measured — trying to delete an ignored file WITHOUT `-x` returns
        // exit 0 and the file stays. The user expects the file they selected by name
        // to be deleted; the "it might be ignored" distinction is meaningless here.
        // The scope is already limited to the given paths, it does not concern the
        // whole repository.
        // `-d`: if an untracked directory was selected, nothing happens without it.
        List<string> arguments = ["clean", "--force", "-d", "-x", "--quiet", "--"];
        arguments.AddRange(paths.Select(path => path.Value));

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);

        return backups;
    }

    public async Task CleanAsync(
        string workingDirectory,
        CleanOptions options,
        bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        options ??= CleanOptions.Default;

        RequireConfirmation(
            userConfirmed,
            "Cleaning the working tree deletes every untracked file and cannot be undone; "
            + "the operation can only be performed with the user's explicit consent.");

        List<string> arguments = ["clean", "--force"];

        if (options.IncludeNestedRepositories)
        {
            // Second `-f`: for nested repositories. A single `-f` silently skips them (measured).
            arguments.Add("--force");
        }

        if (options.IncludeDirectories)
        {
            arguments.Add("-d");
        }

        if (options.OnlyIgnored)
        {
            arguments.Add("-X");
        }
        else if (options.IncludeIgnored)
        {
            arguments.Add("-x");
        }

        arguments.Add("--quiet");

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitIgnoreOutcome> AddToGitIgnoreAsync(
        string workingDirectory,
        RepositoryPath path,
        string pattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        if (await IsTrackedAsync(workingDirectory, path, cancellationToken).ConfigureAwait(false))
        {
            return GitIgnoreOutcome.PathIsTracked;
        }

        if (await IsIgnoredAsync(workingDirectory, path, cancellationToken).ConfigureAwait(false))
        {
            return GitIgnoreOutcome.AlreadyIgnored;
        }

        string file = Path.Combine(workingDirectory, ".gitignore");
        string existing = File.Exists(file)
            ? await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)
            : string.Empty;

        StringBuilder builder = new(existing);

        // 🔴 MEASURED: if the file does not end with a newline, the new pattern
        // STICKS to the previous one (`build/` + `/root.txt` → `build//root.txt`).
        // The result is not just that the new pattern fails to work: it also
        // corrupts the user's existing pattern.
        if (builder.Length > 0 && builder[^1] is not ('\n' or '\r'))
        {
            builder.Append('\n');
        }

        builder.Append(pattern).Append('\n');

        await File.WriteAllTextAsync(file, builder.ToString(), cancellationToken).ConfigureAwait(false);

        return GitIgnoreOutcome.Added;
    }

    public async Task<IReadOnlyList<DiscardBackup>> DiscardPartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        bool userConfirmed,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(selection);

        RequireConfirmation(
            userConfirmed,
            "Reverting the changes on the selected lines deletes content in the working tree. "
            + "The operation can only be performed with the user's explicit consent.");

        // Since it will be applied in reverse, the patch is generated in the "stage"
        // direction: generating the patch and applying it are separate decisions (P05-T04).
        string? patch = PatchBuilder.Build(diff, selection, PatchDirection.Stage);

        if (patch is null)
        {
            // Nothing was selected.
            return [];
        }

        IReadOnlyList<DiscardBackup> backups =
            await BackupAsync(workingDirectory, [diff.Path], cancellationToken).ConfigureAwait(false);

        // ⚠️ NO `--cached`: the patch must be applied only to the working tree (measured).
        await _writer
            .RunAsync(
                workingDirectory,
                ["apply", "--reverse", "-"],
                patch,
                contentEncoding,
                cancellationToken)
            .ConfigureAwait(false);

        return backups;
    }

    public async Task<IReadOnlyList<DiscardBackup>> RestoreBackupsAsync(
        string workingDirectory,
        IReadOnlyList<DiscardBackup> backups,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(backups);

        if (backups.Count == 0)
        {
            return [];
        }

        // 🔴 MEASURED: a separate `cat-file -p` process per backup takes **671 ms**
        // for 200 files, versus **9 ms** with `--batch` in a single process (75×).
        // Recovery is an operation the user is waiting on; undoing a large reset
        // should not take seconds.
        StringBuilder request = new();

        foreach (DiscardBackup backup in backups)
        {
            request.Append(backup.BlobId).Append('\n');
        }

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["cat-file", "--batch"],
                IsReadOnly = true,
                StandardInput = System.Text.Encoding.ASCII.GetBytes(request.ToString()),
            },
            cancellationToken).ConfigureAwait(false);

        List<DiscardBackup> restored = new(backups.Count);
        int offset = 0;

        foreach (DiscardBackup backup in backups)
        {
            if (!TryReadBatchEntry(result.StandardOutput, ref offset, out ReadOnlyMemory<byte> content))
            {
                // The object has been pruned (`gc --prune=now`) → git writes `<oid> missing`.
                // A backup that cannot be recovered is not an error; continue with the rest.
                continue;
            }

            string target = Path.Combine(workingDirectory, backup.Path.Value);

            // The directory of the deleted file may have been deleted too (`clean -d`).
            string? directory = Path.GetDirectoryName(target);

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(target, content, cancellationToken).ConfigureAwait(false);

            restored.Add(backup);
        }

        return restored;
    }

    /// <summary>
    /// Reads the content of the next object from a <c>cat-file --batch</c> stream.
    /// </summary>
    /// <remarks>
    /// Format: <c>&lt;oid&gt; &lt;type&gt; &lt;size&gt;\n&lt;content&gt;\n</c>, and
    /// <c>&lt;oid&gt; missing\n</c> for an object that cannot be found. The content is
    /// taken <b>as bytes</b>: since the size is written in the header, there is no
    /// need to search for a delimiter in binary data — the whole point of this task is
    /// for the backup to be byte-for-byte (P05-T15).
    /// </remarks>
    /// <returns><see langword="true"/> if the object was read; <see langword="false"/> if it is missing.</returns>
    private static bool TryReadBatchEntry(
        byte[] stream,
        ref int offset,
        out ReadOnlyMemory<byte> content)
    {
        content = default;

        int lineEnd = Array.IndexOf(stream, (byte)'\n', offset);

        if (lineEnd < 0)
        {
            return false;
        }

        string header = System.Text.Encoding.ASCII.GetString(stream, offset, lineEnd - offset);
        offset = lineEnd + 1;

        string[] parts = header.Split(' ');

        // `<oid> missing` — if there are not three fields, there is no content either.
        if (parts.Length < 3 || !int.TryParse(parts[2], out int size))
        {
            return false;
        }

        if (offset + size > stream.Length)
        {
            return false;
        }

        content = stream.AsMemory(offset, size);

        // git writes one more newline after the content.
        offset += size + 1;

        return true;
    }

    /// <summary>
    /// Writes the content to be discarded into the object database.
    /// </summary>
    /// <remarks>
    /// Paths not present on disk (deleted files) are skipped: <c>hash-object</c> fails
    /// on them, and they have no content to revert anyway.
    /// </remarks>
    public Task<IReadOnlyList<DiscardBackup>> BackupPathsAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default) =>
        BackupAsync(workingDirectory, paths, cancellationToken);

    private async Task<IReadOnlyList<DiscardBackup>> BackupAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken)
    {
        List<RepositoryPath> existing =
        [
            .. paths.Where(path => File.Exists(Path.Combine(workingDirectory, path.Value))),
        ];

        List<DiscardBackup> backups = new(existing.Count);

        for (int offset = 0; offset < existing.Count; offset += BackupBatchSize)
        {
            List<RepositoryPath> batch =
                [.. existing.Skip(offset).Take(BackupBatchSize)];

            // 🔴 `--no-filters` IS REQUIRED (measured in P05-T15). Without it, git
            // applies "clean" filters while writing the backup and the backup is
            // **not byte-for-byte**:
            //   · if `.gitattributes` has `text=auto`, CRLF → LF (line endings
            //     silently change when written back),
            //   · if there is a custom clean filter (how Git LFS operates), the
            //     backup ends up with **the filter's output, not the file itself** —
            //     in the measurement, content of `SECRET password` became
            //     `*** password` in the backup.
            // A backup that promises recovery but changes the content is worse than
            // taking no backup at all: the user thinks they recovered it.
            List<string> arguments = ["hash-object", "-w", "--no-filters", "--"];
            arguments.AddRange(batch.Select(path => path.Value));

            GitResult result = await _runner.RunCheckedAsync(
                new GitCommand
                {
                    WorkingDirectory = workingDirectory,
                    Arguments = arguments,

                    // Writes an object but does not touch the index; it does not need to go through the queue.
                    IsReadOnly = false,
                },
                cancellationToken).ConfigureAwait(false);

            string[] hashes = result.GetStandardOutputText()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (hashes.Length != batch.Count)
            {
                // If the alignment is broken, it is better to offer nothing than to offer the wrong content as a "backup".
                throw new GitException(
                    GitFailureKind.Unknown,
                    "The number of backed-up contents did not match the number of paths.",
                    "git hash-object -w",
                    result.ExitCode,
                    result.StandardError);
            }

            backups.AddRange(batch.Select((path, index) => new DiscardBackup
            {
                Path = path,
                BlobId = hashes[index].Trim(),
            }));
        }

        return backups;
    }

    private async Task<bool> IsTrackedAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["ls-files", "-z", "--", path.Value],
            },
            cancellationToken).ConfigureAwait(false);

        return result.StandardOutput.Length > 0;
    }

    private async Task<bool> IsIgnoredAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["check-ignore", "--quiet", "--", path.Value],

                // `check-ignore` returns 1 when there is no match; this is not an error, it's the answer.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    private static void RequireConfirmation(bool userConfirmed, string message)
    {
        if (!userConfirmed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
