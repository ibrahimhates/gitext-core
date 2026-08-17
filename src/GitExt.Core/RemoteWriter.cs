using System.Text;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Options for adding a remote (P06-T05).
/// </summary>
public sealed record RemoteAddOptions
{
    /// <summary>Name of the remote to add.</summary>
    public required string Name { get; init; }

    /// <summary>Fetch URL'si.</summary>
    public required string Url { get; init; }

    /// <summary>
    /// Should a <c>fetch</c> run right after adding? (<c>git remote add -f</c>)
    /// </summary>
    /// <remarks>
    /// Default <b>off</b>: this flag <b>puts the command on the network</b> and it may ask for
    /// authentication. The "add remote" button freezing would be unexpected
    /// (progress/cancellation for network operations is P06-T10).
    /// </remarks>
    public bool FetchAfterAdd { get; init; }
}

/// <summary>
/// A remote as it was read <b>before</b> deletion, together with the way back (P06-T05).
/// </summary>
/// <remarks>
/// 🔴 <b>Why does this exist?</b> MEASURED: <c>git remote remove</c> can be an irrecoverable
/// loss — for a commit that lives only on a remote tracking branch, <c>refs/remotes/*</c>
/// <b>and their reflogs</b> are deleted, the commit becomes "unreachable", and after
/// <c>gc --prune=now</c> the <b>object is gone</b>. On top of that <c>branch.*.remote</c>,
/// <c>branch.*.merge</c>, <c>branch.*.pushRemote</c> and <c>remote.pushDefault</c> are silently
/// deleted.
/// <para>
/// ⚠️ The <b>difference</b> from deleting a branch (P06-T03): there the recovery command brought
/// the objects back, here it does not — recovery needs a <c>fetch</c>, which means the <b>remote
/// must still be reachable</b>. The text shown to the user has to say so.
/// </para>
/// </remarks>
public sealed record RemoteRemovalPlan
{
    /// <summary>The remote's state just before deletion.</summary>
    public required GitRemote Remote { get; init; }

    /// <summary>
    /// Local branches whose upstream points at this remote: (branch, short upstream name).
    /// </summary>
    public IReadOnlyList<(string Branch, string Upstream)> AffectedBranches { get; init; } = [];

    /// <summary>Short names of the remote tracking branches pointing at this remote.</summary>
    public IReadOnlyList<string> TrackingBranches { get; init; } = [];

    /// <summary><see langword="true"/> if <c>remote.pushDefault</c> names this remote.</summary>
    public bool IsPushDefault { get; init; }

    /// <summary>
    /// Recovery commands the user <b>can run as they are</b>.
    /// </summary>
    /// <remarks>
    /// The P05-T15 rule: for an irrecoverable operation, if the user can see a runnable way back
    /// on screen, a checkbox is enough instead of a separate "are you sure" dialog.
    /// </remarks>
    public IReadOnlyList<string> RecoveryCommands { get; init; } = [];
}

/// <summary>
/// The result of a rename (P06-T05).
/// </summary>
/// <param name="OldName">The name before the rename.</param>
/// <param name="NewName">The new name.</param>
/// <param name="Warnings">
/// Warnings git emitted alongside <b>exit code 0</b>. This may well be non-empty!
/// </param>
public sealed record RemoteRenameResult(string OldName, string NewName, IReadOnlyList<string> Warnings);

/// <summary>Which direction the URL is for.</summary>
public enum RemoteUrlKind
{
    /// <summary><c>remote.&lt;ad&gt;.url</c></summary>
    Fetch,

    /// <summary><c>remote.&lt;ad&gt;.pushurl</c></summary>
    Push,
}

/// <summary>
/// Remote write operations (P06-T05).
/// </summary>
public interface IRemoteWriter
{
    /// <summary>Adds a new remote.</summary>
    /// <exception cref="ArgumentException">The name is invalid.</exception>
    /// <exception cref="GitException">The name already exists or clashes with another one.</exception>
    Task AddAsync(
        string workingDirectory,
        RemoteAddOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Works out what the deletion will cost and how to recover, <b>without</b> deleting.
    /// </summary>
    Task<RemoteRemovalPlan> PrepareRemovalAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the remote and returns the plan computed <b>before</b> the deletion.
    /// </summary>
    /// <remarks>
    /// The plan is deliberately the return value: once the information is deleted it <b>cannot be
    /// read</b>, and the caller cannot be trusted to remember to make a separate call first.
    /// </remarks>
    Task<RemoteRemovalPlan> RemoveAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Renames the remote.</summary>
    /// <exception cref="ArgumentException">The new name is invalid.</exception>
    /// <exception cref="GitException">The name already exists, or the remote was not found.</exception>
    Task<RemoteRenameResult> RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a remote's <b>single</b> URL.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The remote has more than one URL. MEASURED: in that case <c>git remote set-url</c> says
    /// <c>remote.&lt;name&gt;.url has multiple values</c> and stops with exit code 128; which one
    /// to change is for the <b>user</b> to pick.
    /// </exception>
    Task SetUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a second (third…) URL in the same direction.</summary>
    Task AddUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the given URL.
    /// </summary>
    /// <remarks>
    /// MEASURED: git does not allow the last fetch URL to be deleted
    /// (<c>Will not delete all non-push URLs</c>, exit code 128).
    /// </remarks>
    Task RemoveUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The <c>git remote</c> write wrapper (P06-T05).
/// </summary>
/// <remarks>
/// <para>
/// <b>Commands carry the <c>--</c> separator.</b> MEASURED: a name starting with <c>-</c>
/// is <b>taken for a flag</b> when the separator is missing (<c>error: unknown switch 'x'</c>,
/// exit code 129), and the same name is accepted with <c>--</c>. Our own validation rejects such
/// names, but a remote that <b>already exists</b> in the repository may be named that way.
/// <c>remove</c> and <c>rename</c> are the exception on old git — see
/// <see cref="NameSeparatorSupport"/>.
/// </para>
/// <para>
/// These writes use <c>config.lock</c>, not <c>index.lock</c>; they still go through
/// <see cref="IGitWriter"/> — that is the single entrance to the write path (P05-T03) and the
/// retry on a lock collision comes from there.
/// </para>
/// </remarks>
public sealed class RemoteWriter : IRemoteWriter
{
    /// <summary>
    /// The first version where <c>git remote remove</c> and <c>git remote rename</c> accept the
    /// <c>--</c> separator.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED (git 2.30.2, the ADR-0002 minimum):</b> <c>git remote remove -- origin</c> stops
    /// with exit code <b>129</b> and prints nothing but <c>usage: git remote remove &lt;name&gt;</c>.
    /// The reason is in git's own source: up to and including 2.37 the <c>rm</c> and <c>mv</c>
    /// subcommands <b>do not call <c>parse_options</c> at all</b>, they only check the argument count,
    /// so <c>--</c> counts as one argument too many. They moved to <c>parse_options</c> in
    /// <b>2.38.0</b>.
    /// <para>
    /// Dropping the separator below that version is <b>safe</b> exactly because of the same reason:
    /// with no option parsing, a name starting with <c>-</c> is not taken for a flag either
    /// (measured: <c>git remote remove -eski</c> succeeds on 2.30.2, while on 2.55 the same command
    /// fails with <c>unknown switch 'e'</c>). So each version gets the form that works on it.
    /// </para>
    /// <para>
    /// The version is read, not probed: the failure of the probe would be a <b>write command</b>
    /// entering the queue and coming back with 129, and telling that apart from a real usage error
    /// is only possible from the text.
    /// </para>
    /// </remarks>
    private static readonly GitVersion NameSeparatorSupport = new(2, 38, 0);

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly IRemoteReader _reader;
    private readonly GitVersion _version;

    public RemoteWriter(
        IGitWriter writer,
        IGitProcessRunner runner,
        IRemoteReader reader,
        GitExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(executable);

        _writer = writer;
        _runner = runner;
        _reader = reader;
        _version = executable.Version;
    }

    public async Task AddAsync(
        string workingDirectory,
        RemoteAddOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateName(options.Name, nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Url);
        await EnsureNameDoesNotNestWithExistingRemoteAsync(
            workingDirectory, options.Name, cancellationToken).ConfigureAwait(false);

        List<string> arguments = ["remote", "add"];

        if (options.FetchAfterAdd)
        {
            arguments.Add("-f");
        }

        arguments.Add("--");
        arguments.Add(options.Name);
        arguments.Add(options.Url);

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RemoteRemovalPlan> PrepareRemovalAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        GitRemote remote = await _reader.FindAsync(workingDirectory, name, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new GitException(
                GitFailureKind.RemoteNotFound,
                GitFailureClassifier.Describe(GitFailureKind.RemoteNotFound),
                "git remote remove",
                exitCode: 2,
                standardError: $"error: No such remote: '{name}'");

        IReadOnlyList<(string Branch, string Upstream)> affected =
            await ReadAffectedBranchesAsync(workingDirectory, name, cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<string> tracking =
            await ReadTrackingBranchesAsync(workingDirectory, name, cancellationToken)
                .ConfigureAwait(false);

        string? pushDefault = await ReadConfigAsync(
                workingDirectory, "remote.pushDefault", cancellationToken)
            .ConfigureAwait(false);

        return new RemoteRemovalPlan
        {
            Remote = remote,
            AffectedBranches = affected,
            TrackingBranches = tracking,
            IsPushDefault = string.Equals(pushDefault, name, StringComparison.Ordinal),
            RecoveryCommands = BuildRecoveryCommands(remote, affected, pushDefault),
        };
    }

    public async Task<RemoteRemovalPlan> RemoveAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        // 🔴 The plan is computed BEFORE the deletion. Afterwards the config keys and the remote
        // tracking branches are GONE — there is nothing left to read back (measured).
        RemoteRemovalPlan plan =
            await PrepareRemovalAsync(workingDirectory, name, cancellationToken).ConfigureAwait(false);

        await _writer
            .RunAsync(workingDirectory, BuildNameArguments("remove", name), cancellationToken)
            .ConfigureAwait(false);

        return plan;
    }

    public async Task<RemoteRenameResult> RenameAsync(
        string workingDirectory,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ValidateName(newName, nameof(newName));

        GitResult result = await _writer
            .RunAsync(workingDirectory, BuildNameArguments("rename", oldName, newName), cancellationToken)
            .ConfigureAwait(false);

        // 🔴 MEASURED: git DOES NOT UPDATE a non-default fetch refspec, yet the exit code is
        // still 0. The warning sits on stderr alone:
        //   warning: Not updating non-default fetch refspec
        // A UI that looks only at the exit code would say "renamed successfully" while the user's
        // fetch configuration stayed bound to the old name (the same trap as `switch --merge` in
        // P06-T02).
        return new RemoteRenameResult(oldName, newName, ExtractWarnings(result.StandardError));
    }

    public async Task SetUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        // MEASURED: with more than one URL, git refuses a plain `set-url`
        // ("has multiple values", exit code 128). Rather than take the error from git we stop
        // here, so the UI can ask the user WHICH URL.
        GitRemote? remote = await _reader.FindAsync(workingDirectory, name, cancellationToken)
            .ConfigureAwait(false);

        if (remote is not null)
        {
            IReadOnlyList<string> existing =
                kind == RemoteUrlKind.Fetch ? remote.FetchUrls : remote.PushUrls;

            if (existing.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Remote '{name}' has {existing.Count} URLs configured; "
                    + "it cannot be updated in a single step without choosing which one to change.");
            }
        }

        await RunUrlCommandAsync(workingDirectory, name, kind, url, mode: null, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task AddUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default) =>
        RunUrlCommandAsync(workingDirectory, name, kind, url, "--add", cancellationToken);

    public Task RemoveUrlAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        CancellationToken cancellationToken = default) =>
        RunUrlCommandAsync(workingDirectory, name, kind, url, "--delete", cancellationToken);

    private async Task RunUrlCommandAsync(
        string workingDirectory,
        string name,
        RemoteUrlKind kind,
        string url,
        string? mode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        List<string> arguments = ["remote", "set-url"];

        if (mode is not null)
        {
            arguments.Add(mode);
        }

        if (kind == RemoteUrlKind.Push)
        {
            arguments.Add("--push");
        }

        arguments.Add("--");
        arguments.Add(name);
        arguments.Add(url);

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateName(string? name, string parameterName)
    {
        // Name validation is not left to git: git's answer is exit code 128 and free text,
        // whereas the UI must be able to say "why it is invalid" WHILE the user types (the
        // P06-T01 pattern).
        if (RemoteName.Validate(name) is { } problem)
        {
            throw new ArgumentException(
                $"'{name}' is not a valid remote name ({RemoteName.Describe(problem)})",
                parameterName);
        }
    }

    private async Task EnsureNameDoesNotNestWithExistingRemoteAsync(
        string workingDirectory,
        string candidate,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GitRemote> remotes = await _reader.ReadAllAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        foreach (GitRemote remote in remotes)
        {
            if (string.Equals(remote.Name, candidate, StringComparison.Ordinal))
            {
                continue;
            }

            if (candidate.StartsWith(remote.Name + "/", StringComparison.Ordinal)
                || remote.Name.StartsWith(candidate + "/", StringComparison.Ordinal))
            {
                throw new GitException(
                    GitFailureKind.RemoteNameConflict,
                    GitFailureClassifier.Describe(GitFailureKind.RemoteNameConflict),
                    "git remote add",
                    exitCode: 3,
                    standardError:
                    $"error: remote '{candidate}' is a subset/superset conflict with existing '{remote.Name}'");
            }
        }
    }

    /// <summary>
    /// Builds the arguments of a <c>remote</c> subcommand that takes only names, putting the
    /// <c>--</c> separator in only where git understands it.
    /// </summary>
    /// <seealso cref="NameSeparatorSupport"/>
    private List<string> BuildNameArguments(string subcommand, params string[] names)
    {
        List<string> arguments = ["remote", subcommand];

        if (_version >= NameSeparatorSupport)
        {
            arguments.Add("--");
        }

        arguments.AddRange(names);

        return arguments;
    }

    /// <summary>
    /// Local branches whose upstream points at this remote.
    /// </summary>
    /// <remarks>
    /// <c>branch.&lt;branch&gt;.remote</c> is read; because <c>for-each-ref</c>'s
    /// <c>%(upstream:short)</c> field comes back <b>empty</b> after the deletion, this information
    /// can only be collected now.
    /// </remarks>
    private async Task<IReadOnlyList<(string Branch, string Upstream)>> ReadAffectedBranchesAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(
                workingDirectory,
                "for-each-ref",
                "--format=%(refname:short)%00%(upstream:remotename)%00%(upstream:short)",
                "refs/heads"),
            cancellationToken).ConfigureAwait(false);

        List<(string, string)> affected = [];

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split('\0');

            if (fields.Length == 3
                && string.Equals(fields[1], name, StringComparison.Ordinal)
                && fields[2].Length > 0)
            {
                affected.Add((fields[0], fields[2]));
            }
        }

        return affected;
    }

    private async Task<IReadOnlyList<string>> ReadTrackingBranchesAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(
                workingDirectory,
                "for-each-ref",
                "--format=%(refname:short)",
                RemoteName.RemotesPrefix + name),
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. result.GetStandardOutputText()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r')),
        ];
    }

    private async Task<string?> ReadConfigAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", "--get", key],
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string value = result.GetStandardOutputText().Trim('\n', '\r');

        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// Commands that restore the deleted configuration and are <b>runnable as they are</b>.
    /// </summary>
    private static IReadOnlyList<string> BuildRecoveryCommands(
        GitRemote remote,
        IReadOnlyList<(string Branch, string Upstream)> affected,
        string? pushDefault)
    {
        List<string> commands = [];

        string first = remote.FetchUrls.Count > 0 ? remote.FetchUrls[0] : string.Empty;
        commands.Add($"git remote add {Quote(remote.Name)} {Quote(first)}");

        foreach (string url in remote.FetchUrls.Skip(1))
        {
            commands.Add($"git remote set-url --add {Quote(remote.Name)} {Quote(url)}");
        }

        foreach (string url in remote.PushUrls)
        {
            string flag = url == remote.PushUrls[0] ? "--push" : "--push --add";
            commands.Add($"git remote set-url {flag} {Quote(remote.Name)} {Quote(url)}");
        }

        // `remote add` already sets up the default refspec; it is only written when it differs.
        if (!remote.HasDefaultFetchRefspec)
        {
            foreach (string refspec in remote.FetchRefspecs)
            {
                commands.Add(
                    $"git config --add remote.{remote.Name}.fetch {Quote(refspec)}");
            }
        }

        if (remote.TagOption is { } tagOption)
        {
            commands.Add($"git config remote.{remote.Name}.tagopt {Quote(tagOption)}");
        }

        // ⚠️ The objects DO NOT COME BACK with `remote add`: the remote tracking branches were
        // deleted and their reflogs went with them. A fresh fetch is required — so the remote has
        // to be reachable.
        commands.Add($"git fetch {Quote(remote.Name)}");

        foreach ((string branch, string upstream) in affected)
        {
            commands.Add($"git branch --set-upstream-to={Quote(upstream)} {Quote(branch)}");
        }

        if (string.Equals(pushDefault, remote.Name, StringComparison.Ordinal))
        {
            commands.Add($"git config remote.pushDefault {Quote(remote.Name)}");
        }

        return commands;
    }

    /// <summary>
    /// Turns the command text into something that can be pasted into a shell.
    /// </summary>
    /// <remarks>
    /// For <b>display</b> only; our own calls pass arguments as an array and never go through a
    /// shell (ADR-0002).
    /// </remarks>
    private static string Quote(string value)
    {
        if (value.Length == 0)
        {
            return "''";
        }

        bool safe = value.All(c =>
            char.IsLetterOrDigit(c) || c is '/' or '.' or '_' or '-' or ':' or '@' or '+' or '~' or '*');

        return safe ? value : "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static IReadOnlyList<string> ExtractWarnings(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return [];
        }

        List<string> warnings = [];
        StringBuilder current = new();

        foreach (string raw in standardError.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.StartsWith("warning:", StringComparison.Ordinal))
            {
                Flush(warnings, current);
                current.Append(line["warning:".Length..].Trim());
            }
            else if (current.Length > 0 && line.Trim().Length > 0)
            {
                // git can spread the warning over several lines: after the "Not updating
                // non-default fetch refspec" line come the refspec itself and "Please update…".
                current.Append(' ').Append(line.Trim());
            }
        }

        Flush(warnings, current);

        return warnings;

        static void Flush(List<string> target, StringBuilder buffer)
        {
            if (buffer.Length > 0)
            {
                target.Add(buffer.ToString());
                buffer.Clear();
            }
        }
    }
}
