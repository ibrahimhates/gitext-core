namespace GitExt.Core.Git;

/// <summary>
/// Which file a <c>git config</c> setting will be written to (P08-T15).
/// </summary>
public enum GitConfigScope
{
    /// <summary>Only this repository (<c>.git/config</c>).</summary>
    Local,

    /// <summary>All of the user's repositories (<c>~/.gitconfig</c>).</summary>
    Global,
}

/// <summary>
/// Writes <c>git config</c> settings (P08-T15).
/// </summary>
public interface IGitConfigWriter
{
    /// <summary>
    /// Reads the <b>raw</b> value in a specific scope (not the combined one).
    /// </summary>
    /// <remarks>
    /// Needed to populate the "local" and "global" fields of the settings screen:
    /// <see cref="IGitConfigReader"/> gives the combined value and that combination does
    /// <b>not tell</b> which file the value came from. Showing the user a global value in the
    /// local field meant that on save they would unknowingly create a local copy.
    /// </remarks>
    Task<string?> GetScopedAsync(
        string workingDirectory,
        string key,
        GitConfigScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the setting; if <paramref name="value"/> is empty it <b>removes</b> the setting.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>An empty value means "delete", not "set to empty".</b> Measured:
    /// <c>git config user.name ""</c> gives exit code 0 and the setting ends up <b>present but
    /// empty</b> — <c>--get</c> returns it with exit code 0 and empty output. Committing with an
    /// empty <c>user.name</c> produces a different and worse error than never having set it at
    /// all. When the user clears the field, what they mean is "delete".
    /// </remarks>
    Task SetAsync(
        string workingDirectory,
        string key,
        string value,
        GitConfigScope scope,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IGitConfigWriter"/>
public sealed class GitConfigWriter : IGitConfigWriter
{
    /// <summary>
    /// The "no such key" exit code of <c>--unset</c>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED:</b> not 0 or 1, but <b>5</b>. If it counted as an error, clearing a field
    /// that was already empty would show the user an error — while nothing had gone wrong at all.
    /// </remarks>
    private const int UnsetMissingKeyExitCode = 5;

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public GitConfigWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<string?> GetScopedAsync(
        string workingDirectory,
        string key,
        GitConfigScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", ScopeFlag(scope), "--get", key],

                // 🔴 MEASURED. 1 = "not set" — it is also 1 when the global file does not exist
                // at all, which is not an error. 128 = `--local` outside a repository
                // (`fatal: --local can only be used inside a git repository`); the UI does not
                // offer this, but a directory given on the command line may not be a repository
                // and we must not crash because of it.
                SuccessExitCodes = [0, 1, 128],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string value = result.GetStandardOutputText().Trim('\n', '\r');

        // An empty string is NOT converted to `null` here: "present but empty" is a real state
        // and the UI has to be able to show and fix it.
        return value;
    }

    public Task SetAsync(
        string workingDirectory,
        string key,
        string value,
        GitConfigScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return string.IsNullOrEmpty(value)
            ? UnsetAsync(workingDirectory, key, scope, cancellationToken)
            : _writer.RunAsync(
                workingDirectory,
                ["config", ScopeFlag(scope), key, value],
                cancellationToken);
    }

    private async Task UnsetAsync(
        string workingDirectory,
        string key,
        GitConfigScope scope,
        CancellationToken cancellationToken)
    {
        try
        {
            await _writer
                .RunAsync(workingDirectory, ["config", ScopeFlag(scope), "--unset", key], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException ex) when (ex.ExitCode == UnsetMissingKeyExitCode)
        {
            // The key does not exist already. The desired end state is satisfied; propagating
            // this as an error would show a pointless error to a user clearing an empty field.
        }
    }

    private static string ScopeFlag(GitConfigScope scope) =>
        scope == GitConfigScope.Global ? "--global" : "--local";
}
