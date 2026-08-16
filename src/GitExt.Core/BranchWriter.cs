using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Branch creation options (P06-T01).
/// </summary>
public sealed record BranchCreateOptions
{
    /// <summary>Name of the branch to create.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Starting point: commit hash, branch, or tag name. <see langword="null"/> means
    /// <c>HEAD</c>.
    /// </summary>
    public string? StartPoint { get; init; }

    /// <summary>
    /// Switch to the branch after creating it? Defaults to <see langword="true"/> —
    /// GitExtensions also comes with <c>chkCheckoutAfterCreate</c> checked (§ 9).
    /// </summary>
    public bool Checkout { get; init; } = true;
}

/// <summary>
/// Result of creating a branch (P06-T01).
/// </summary>
/// <param name="Name">Name of the created branch.</param>
/// <param name="CheckedOut">Was the branch switched to?</param>
/// <param name="Upstream">
/// Upstream that git set up <b>on its own</b>, or <see langword="null"/> if none was set up.
/// </param>
public sealed record BranchCreateResult(string Name, bool CheckedOut, string? Upstream);


/// <summary>
/// What to do with local changes when switching branches (P06-T02).
/// </summary>
/// <remarks>
/// Order follows GitExtensions' <c>FormCheckoutBranch</c> "Local changes" group (§ 9):
/// <i>Don't change · Merge · Stash · Reset</i>.
/// </remarks>
public enum LocalChangesAction
{
    /// <summary>Don't touch. git carries changes over if it can, and refuses otherwise.</summary>
    Keep,

    /// <summary>
    /// <c>--merge</c>: try to merge the changes into the target.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED:</b> on conflict this path returns <b>exit code 0</b> and leaves the tree
    /// <b>unmerged</b> (while also leaving behind a hidden autostash). A UI that looks only at
    /// the exit code would say "switched successfully".
    /// </remarks>
    Merge,

    /// <summary>
    /// Set aside with <c>git stash push -u</c>.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED — this is the most capable option.</b> Because <c>-u</c> also picks up
    /// untracked files, it resolves the <i>"an untracked file would be overwritten"</i> conflict
    /// too; <see cref="Discard"/> <b>refuses</b> in that case. It's also not destructive.
    /// </remarks>
    Stash,

    /// <summary>
    /// <c>--discard-changes</c>: throw away local changes.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>DESTRUCTIVE AND CONFIRMED BY MEASUREMENT:</b> staged content survives in the object
    /// database as a dangling blob, but <b>unstaged content leaves no trace whatsoever</b> —
    /// exactly the <c>git clean</c> situation from P05-T15. This is why a backup is taken first.
    /// </remarks>
    Discard,
}

/// <summary>
/// Branch switch options (P06-T02).
/// </summary>
public sealed record BranchSwitchOptions
{
    /// <summary>Target: branch name or commit.</summary>
    public required string Target { get; init; }

    /// <summary>Go straight to the commit instead of a branch (detached HEAD).</summary>
    public bool Detach { get; init; }

    /// <summary>What to do with local changes?</summary>
    public LocalChangesAction LocalChanges { get; init; } = LocalChangesAction.Keep;

    /// <summary>
    /// <b>Required</b> explicit confirmation for <see cref="LocalChangesAction.Discard"/>.
    /// </summary>
    public bool UserConfirmed { get; init; }
}

/// <summary>
/// Result of switching branches (P06-T02).
/// </summary>
public sealed record BranchSwitchResult
{
    public required string Target { get; init; }

    /// <summary>
    /// Did the tree end up with <b>unmerged</b> files?
    /// </summary>
    /// <remarks>
    /// The exit code doesn't say (measured); status is read separately.
    /// </remarks>
    public bool HasConflicts { get; init; }

    /// <summary>Were local changes moved into a stash?</summary>
    public bool StashCreated { get; init; }

    /// <summary>Backups of discarded content (only for <see cref="LocalChangesAction.Discard"/>).</summary>
    public IReadOnlyList<DiscardBackup> Backups { get; init; } = [];
}


/// <summary>
/// Result of deleting a branch (P06-T03).
/// </summary>
public sealed record BranchDeleteResult
{
    public required string Name { get; init; }

    /// <summary>
    /// Last commit the deleted branch pointed to.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>This is the only reliable way to recover it.</b> MEASURED: deleting a branch also
    /// deletes <b>its own reflog</b>, and having a trace in the HEAD reflog only holds if that
    /// branch was worked on <b>in this working tree</b>. When a branch produced in a linked
    /// worktree is deleted, <b>no reflog trace remains at all</b> — the commit can only be
    /// reached via <c>fsck --unreachable</c>. This is why the hash is read <b>before</b>
    /// deleting and handed back to the user.
    /// </remarks>
    public required string LastCommitId { get; init; }

    /// <summary>Did the branch need <c>--force</c> (i.e. it wasn't merged)?</summary>
    public bool WasUnmerged { get; init; }
}

/// <summary>
/// Used to distinguish the reason when branch deletion is refused (P06-T03).
/// </summary>
public sealed class BranchNotMergedException : Exception
{
    public BranchNotMergedException(string name, string lastCommitId)
        : base($"Branch '{name}' contains commits that are not merged anywhere.")
    {
        Name = name;
        LastCommitId = lastCommitId;
    }

    public string Name { get; }

    /// <summary>Tip of the branch — shown to the user as the recovery path.</summary>
    public string LastCommitId { get; }
}

/// <summary>
/// Branch write operations (P06-T01).
/// </summary>
public interface IBranchWriter
{
    /// <summary>
    /// Creates a new branch, switching to it if requested.
    /// </summary>
    /// <exception cref="ArgumentException">Name is invalid.</exception>
    /// <exception cref="GitException">
    /// The branch already exists, the name conflicts, the starting point could not be
    /// resolved, or the working tree is dirty.
    /// </exception>
    Task<BranchCreateResult> CreateAsync(
        string workingDirectory,
        BranchCreateOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches to another branch or commit (P06-T02).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="LocalChangesAction.Discard"/> was selected but not confirmed.
    /// </exception>
    /// <exception cref="GitException">The target could not be resolved or the switch was refused.</exception>
    Task<BranchSwitchResult> SwitchAsync(
        string workingDirectory,
        BranchSwitchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames the branch (P06-T03).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Forced (<c>-M</c>) renaming is NOT offered.</b> MEASURED: renaming with <c>-M</c>
    /// onto an existing name <b>silently overwrites</b> that branch — in the measurement the
    /// target branch vanished with no warning at all. A name collision is reported as an error.
    /// </remarks>
    /// <exception cref="ArgumentException">New name is invalid.</exception>
    /// <exception cref="GitException">Name already exists, or the branch was not found.</exception>
    Task RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the branch (P06-T03).
    /// </summary>
    /// <param name="workingDirectory">Repository working directory.</param>
    /// <param name="name">Name of the branch to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="force">
    /// Delete even an unmerged branch? While <see langword="false"/>, if git refuses,
    /// <see cref="BranchNotMergedException"/> is thrown.
    /// </param>
    /// <exception cref="BranchNotMergedException">The branch is unmerged and force was not set.</exception>
    /// <exception cref="GitException">The branch is checked out in a working tree.</exception>
    Task<BranchDeleteResult> DeleteAsync(
        string workingDirectory,
        string name,
        bool force = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Wrapper around <c>git branch</c> / <c>git switch -c</c> (P06-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED — why two separate commands?</b> The difference isn't just convenience, it's
/// <b>safety</b>:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>git branch</c> <b>never touches</b> the working tree; it always succeeds even on a
///     dirty tree.
///   </description></item>
///   <item><description>
///     <c>git switch -c</c>, on the other hand, performs a checkout and <b>can refuse</b> on a
///     dirty tree (exit code <b>1</b>, distinct from the <b>128</b> of name errors). When it
///     refuses, <b>it doesn't create the branch either</b> — confirmed by measurement: neither
///     did the branch remain nor did it change. So there is no partial result; the user loses
///     nothing.
///   </description></item>
/// </list>
/// <para>
/// <b>MEASURED — upstream gets set up automatically.</b> If the starting point is a remote
/// tracking branch (<c>origin/x</c>), git sets up the upstream itself (the
/// <c>branch.autoSetupMerge</c> default); when created from a local branch it <b>does not</b>
/// set it up. We don't imitate this — we <b>read</b> the actual outcome and report it, because
/// the user's configuration can change it.
/// </para>
/// </remarks>
public sealed class BranchWriter : IBranchWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly IWorkingTreeWriter? _backup;

    /// <param name="writer">For git calls that go through the write queue.</param>
    /// <param name="runner">For read-only git calls.</param>
    /// <param name="backup">
    /// Writer that takes a backup before a destructive switch. If <see langword="null"/>,
    /// <see cref="LocalChangesAction.Discard"/> is <b>refused</b> — content that can't be
    /// recovered is never deleted without a safety net (P05-T15 rule).
    /// </param>
    public BranchWriter(
        IGitWriter writer,
        IGitProcessRunner runner,
        IWorkingTreeWriter? backup = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
        _backup = backup;
    }

    public async Task<BranchCreateResult> CreateAsync(
        string workingDirectory,
        BranchCreateOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Name validation isn't left to git: git's answer is exit code 128 plus free-form
        // text, whereas the UI needs to be able to say "why it's invalid" WHILE the user types.
        if (BranchName.Validate(options.Name) is { } problem)
        {
            throw new ArgumentException(
                $"'{options.Name}' is not a valid branch name ({problem}).", nameof(options));
        }

        // Unborn HEAD is filtered out first: git's message ("not a valid object name: 'main'")
        // would fall into UnknownRevision and tell the user "branch not found" — when the real
        // problem is that the repository is empty (measured).
        if (options.StartPoint is null
            && !await HasCommitsAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
        {
            throw new GitException(
                GitFailureKind.UnbornHead,
                GitFailureClassifier.Describe(GitFailureKind.UnbornHead),
                "git branch",
                exitCode: 128,
                standardError: string.Empty);
        }

        // ⚠️ `--` separator: we already validated the name doesn't start with a dash, but the
        // starting point comes from the user; without the separator a `-x` would be mistaken
        // for an option.
        IReadOnlyList<string> arguments = options.Checkout
            ? ["switch", "--create", options.Name, .. StartPointArgument(options)]
            : ["branch", "--", options.Name, .. StartPointArgument(options)];

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);

        string? upstream = await ReadUpstreamAsync(workingDirectory, options.Name, cancellationToken)
            .ConfigureAwait(false);

        return new BranchCreateResult(options.Name, options.Checkout, upstream);
    }

    public async Task RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);

        if (BranchName.Validate(newName) is { } problem)
        {
            throw new ArgumentException(
                $"'{newName}' is not a valid branch name ({problem}).", nameof(newName));
        }

        // ⚠️ `-m`, NEVER `-M`: measured that `-M` silently wiped out an existing target branch.
        // The upstream and the branch's own reflog are preserved with `-m` (measured).
        await _writer
            .RunAsync(workingDirectory, ["branch", "-m", "--", oldName, newName], cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BranchDeleteResult> DeleteAsync(
        string workingDirectory,
        string name,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // 🔴 Read the hash BEFORE deleting: after deletion the branch's own reflog is also
        // gone, and a branch produced in a linked worktree leaves NO reflog trace at all
        // (measured).
        string lastCommit = await RunTextAsync(
                workingDirectory, ["rev-parse", "--verify", "--quiet", BranchName.HeadsPrefix + name],
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _writer
                .RunAsync(
                    workingDirectory,
                    ["branch", force ? "-D" : "-d", "--", name],
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException error) when (!force && IsNotFullyMerged(error))
        {
            // 🔑 We do NOT compute "merged" ourselves. MEASURED: `-d` deletes a branch that's
            // merged into its **upstream** even if not into HEAD (with a warning, exit code 0).
            // Deciding with `merge-base --is-ancestor … HEAD` would produce a false
            // "not merged" alarm for such branches. git makes the call.
            throw new BranchNotMergedException(name, lastCommit);
        }

        return new BranchDeleteResult
        {
            Name = name,
            LastCommitId = lastCommit,
            WasUnmerged = force,
        };
    }

    private static bool IsNotFullyMerged(GitException error) =>
        error.StandardError.Contains("not fully merged", StringComparison.Ordinal);

    private async Task<string> RunTextAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand { WorkingDirectory = workingDirectory, Arguments = arguments },
            cancellationToken).ConfigureAwait(false);

        return result.GetStandardOutputText().Trim();
    }

    public async Task<BranchSwitchResult> SwitchAsync(
        string workingDirectory,
        BranchSwitchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Target);

        IReadOnlyList<DiscardBackup> backups = [];
        bool stashed = false;

        if (options.LocalChanges == LocalChangesAction.Discard)
        {
            if (!options.UserConfirmed)
            {
                throw new InvalidOperationException(
                    "Switching branches while discarding local changes deletes content irreversibly; "
                    + "the operation can only be performed with the user's explicit consent.");
            }

            if (_backup is null)
            {
                throw new InvalidOperationException(
                    "Local changes cannot be discarded without a backup writer.");
            }

            // 🔴 Unstaged content leaves NO trace at all in the object database (measured);
            // the backup is the only way back from this path.
            backups = await _backup
                .BackupPathsAsync(
                    workingDirectory,
                    await DirtyTrackedPathsAsync(workingDirectory, cancellationToken)
                        .ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (options.LocalChanges == LocalChangesAction.Stash)
        {
            // `-u` IS REQUIRED: without it untracked files stay in the working tree and the
            // "an untracked file would be overwritten" conflict doesn't get resolved (measured).
            stashed = await TryStashAsync(workingDirectory, options.Target, cancellationToken)
                .ConfigureAwait(false);
        }

        List<string> arguments = ["switch"];

        if (options.Detach)
        {
            arguments.Add("--detach");
        }

        if (options.LocalChanges == LocalChangesAction.Merge)
        {
            arguments.Add("--merge");
        }
        else if (options.LocalChanges == LocalChangesAction.Discard)
        {
            arguments.Add("--discard-changes");
        }

        arguments.Add("--");
        arguments.Add(options.Target);

        try
        {
            await _writer.RunAsync(workingDirectory, arguments, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException)
        {
            // If the switch fails after stashing, the user would be left inside a stash they
            // never asked for. We hand the state back as-is.
            if (stashed)
            {
                await PopStashAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        // 🔴 EXIT CODE 0 DOES NOT MEAN NO CONFLICT (measured for `--merge`); status is read
        // separately.
        bool conflicts = await HasUnmergedPathsAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return new BranchSwitchResult
        {
            Target = options.Target,
            HasConflicts = conflicts,
            StashCreated = stashed,
            Backups = backups,
        };
    }

    /// <summary>Changed <b>tracked</b> paths — the ones to back up.</summary>
    /// <remarks>
    /// Untracked files are excluded: <c>--discard-changes</c> <b>doesn't touch</b> them
    /// (measured), so backing them up would be wasted work.
    /// </remarks>
    private async Task<IReadOnlyList<RepositoryPath>> DirtyTrackedPathsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["diff", "--name-only", "-z", "HEAD"],
            },
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. result
                .SplitStandardOutputAtNul()
                .Where(value => value.Length > 0)
                .Select(RepositoryPath.Parse),
        ];
    }

    private async Task<bool> HasUnmergedPathsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // `diff --name-only --diff-filter=U` returns only unmerged paths; the human-readable
        // output isn't parsed.
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["diff", "--name-only", "--diff-filter=U", "-z"],
            },
            cancellationToken).ConfigureAwait(false);

        return result.SplitStandardOutputAtNul().Any(value => value.Length > 0);
    }

    /// <summary>Creates a stash; returns <see langword="false"/> if there was nothing to set aside.</summary>
    private async Task<bool> TryStashAsync(
        string workingDirectory,
        string target,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments =
                [
                    "stash", "push", "--include-untracked", "--quiet",
                    "--message", $"gitext: switch to branch '{target}'",
                ],
                IsReadOnly = false,
            },
            cancellationToken).ConfigureAwait(false);

        // On a clean tree `stash push` doesn't error but doesn't create a stash either; reading
        // the result from the stash list is safer than parsing the output text.
        _ = result;

        return await HasStashAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> HasStashAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", "refs/stash"],
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    private Task PopStashAsync(string workingDirectory, CancellationToken cancellationToken) =>
        _writer.RunAsync(workingDirectory, ["stash", "pop"], cancellationToken);

    private static string[] StartPointArgument(BranchCreateOptions options) =>
        options.StartPoint is { Length: > 0 } start ? [start] : [];

    /// <summary>
    /// <b>Reads</b> whether git set up the upstream on its own.
    /// </summary>
    /// <remarks>
    /// An empty string means the same thing as "no upstream"; <c>for-each-ref</c> returns empty
    /// for both cases.
    /// </remarks>
    private async Task<string?> ReadUpstreamAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments =
                [
                    "for-each-ref",
                    "--format=%(upstream:short)",
                    BranchName.HeadsPrefix + name,
                ],
            },
            cancellationToken).ConfigureAwait(false);

        string upstream = result.GetStandardOutputText().Trim();

        return upstream.Length == 0 ? null : upstream;
    }

    private async Task<bool> HasCommitsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", "HEAD"],

                // Unborn HEAD isn't an error, it's information.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }
}
