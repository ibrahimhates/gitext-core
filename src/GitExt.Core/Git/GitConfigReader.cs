namespace GitExt.Core.Git;

/// <summary>
/// Reads the effective <c>git config</c> values (P05-T13).
/// </summary>
/// <remarks>
/// "Effective" = the combination of system + global + local, i.e. the setting that actually
/// applies for the user in that repository. <c>git config --get</c> already gives that
/// combination; reading the files separately would mean <b>we</b> re-implement the precedence
/// order.
/// </remarks>
public interface IGitConfigReader
{
    /// <summary>
    /// Reads the raw value of a setting; <see langword="null"/> if the setting is missing or empty.
    /// </summary>
    Task<string?> GetAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a <b>path</b> setting; <c>~</c> and <c>~user</c> are expanded.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>MEASURED (P05-T13):</b> plain <c>--get</c> returns the <c>commit.template</c> value
    /// <c>~/.git_commit_msg.txt</c> <b>raw</b>; <c>--path</c> turns the same value into
    /// <c>/home/…/.git_commit_msg.txt</c>. Mistaking the raw value for a file name would make a
    /// template starting with <c>~</c> <b>silently "not found"</b>.
    /// </remarks>
    Task<string?> GetPathAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IGitConfigReader"/>
public sealed class GitConfigReader : IGitConfigReader
{
    private readonly IGitProcessRunner _runner;

    public GitConfigReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public Task<string?> GetAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken = default) =>
        ReadAsync(workingDirectory, key, asPath: false, cancellationToken);

    public Task<string?> GetPathAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken = default) =>
        ReadAsync(workingDirectory, key, asPath: true, cancellationToken);

    private async Task<string?> ReadAsync(
        string workingDirectory,
        string key,
        bool asPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        List<string> arguments = ["config"];

        if (asPath)
        {
            arguments.Add("--path");
        }

        // ⚠️ `--get` is deliberate: if the same key is defined more than once, git's own rule is
        // "last writer wins" and `--get` gives exactly that (measured: `--get-all` gives two
        // lines while `--get` gives the last one). Taking the first line would be silently wrong.
        arguments.Add("--get");
        arguments.Add(key);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = arguments,

                // MEASURED: if the setting is not defined the exit code is 1 and the output is
                // empty. This is not an error, it is the "not present" answer; if it counted as
                // an error every unconfigured repository would throw an exception.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string value = result.GetStandardOutputText().Trim('\n', '\r');

        // Being set to an empty string (`git config commit.template ""` → exit 0, empty output)
        // means the same as "not set"; callers do not need to make this distinction.
        return value.Length == 0 ? null : value;
    }
}
