using System.Text;

namespace GitExt.Core.Git;

/// <summary>
/// The definition of a single <c>git</c> command to be executed.
/// </summary>
/// <remarks>
/// <para>
/// Arguments are kept <b>as an array</b> and are never joined into a single command line string
/// (ADR-0002). User data — file paths, ref names, commit messages — is not exposed to shell
/// interpretation.
/// </para>
/// <para>
/// Free-form text such as a commit message must be passed via <see cref="StandardInput"/>
/// instead of as an argument.
/// </para>
/// </remarks>
public sealed record GitCommand
{
    /// <summary>The directory the command will be run in.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// The arguments passed to <c>git</c>. Each element is a single argument; it may contain spaces.
    /// </summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>
    /// Data to be sent over stdin. If <see langword="null"/>, stdin is closed immediately.
    /// </summary>
    public ReadOnlyMemory<byte>? StandardInput { get; init; }

    /// <summary>
    /// The process is killed if it does not finish within this time.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// <see langword="true"/> if the command does not modify the repository.
    /// </summary>
    /// <remarks>
    /// On read-only calls <c>GIT_OPTIONAL_LOCKS=0</c> is set; this prevents a <c>git status</c>
    /// running in the background from trying to update the index and producing a lock collision.
    /// </remarks>
    public bool IsReadOnly { get; init; } = true;

    /// <summary>
    /// The cases where a non-zero exit code is not considered an error.
    /// </summary>
    /// <remarks>
    /// Some commands report success with a non-zero code; for example <c>git diff --quiet</c>
    /// returns 1 if there is a difference. Those codes are declared here.
    /// </remarks>
    public IReadOnlyCollection<int> SuccessExitCodes { get; init; } = [0];

    /// <summary>
    /// Upper bound for stdout; if exceeded, reading is stopped and the process is terminated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <see langword="null"/> there is no limit. It exists as a safety valve: <c>git diff</c>
    /// can produce <b>hundreds of megabytes</b> of patch for a single commit (measured: a
    /// 12.7 MB file that changed entirely yields a 23 MB patch) and taking that into memory
    /// kills the application.
    /// </para>
    /// <para>
    /// When the limit is exceeded the result is marked with <see cref="GitResult.OutputTruncated"/>;
    /// the caller must <b>not parse</b> the partial output and should choose a different strategy.
    /// </para>
    /// </remarks>
    public long? MaximumOutputBytes { get; init; }

    /// <summary>
    /// Environment variables to be added for this call (P06-T09).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists only for <b>authentication</b>: <c>GIT_ASKPASS</c> and the secret value it will
    /// read. A password cannot be passed as an argument — the command line is visible to every
    /// process on the same machine via <c>ps</c>. An environment variable is visible via
    /// <c>/proc/&lt;pid&gt;/environ</c> only to <b>the same user</b>; this is the route `gh` and
    /// similar tools use as well.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="ToDisplayString"/> does <b>not</b> print these: the command log and the
    /// "show command" area are on screen.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>
    /// Live progress reporting (P06-T10).
    /// </summary>
    /// <remarks>
    /// When set, <c>stderr</c> is read <b>as a stream</b> and every progress line is sent here.
    /// The full text is still accumulated — the existing parsers look at it.
    /// </remarks>
    public IProgress<GitProgress>? Progress { get; init; }

    /// <summary>
    /// Shorthand: creates a command from a working directory and arguments.
    /// </summary>
    public static GitCommand Create(string workingDirectory, params string[] arguments) =>
        new() { WorkingDirectory = workingDirectory, Arguments = arguments };

    /// <summary>
    /// Converts the command into a readable form to be shown in the log and to the user.
    /// </summary>
    /// <remarks>
    /// This output is <b>for display only</b>; it is never fed back into a shell.
    /// Arguments containing spaces or special characters are quoted so that the user can copy
    /// the command into their terminal.
    /// </remarks>
    public string ToDisplayString()
    {
        StringBuilder builder = new("git");

        foreach (string argument in Arguments)
        {
            builder.Append(' ');
            builder.Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string argument)
    {
        if (argument.Length == 0)
        {
            return "''";
        }

        bool needsQuoting = argument.AsSpan().ContainsAny(" \t\n'\"\\$`|&;<>()*?[]{}#~!");
        if (!needsQuoting)
        {
            return argument;
        }

        // POSIX single quoting: an inner single quote is escaped with the '\'' sequence.
        return $"'{argument.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }
}
