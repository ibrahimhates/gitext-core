using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Which transport is used to connect to the remote repository (P06-T09)?</summary>
public enum RemoteTransport
{
    /// <summary>Unrecognized format.</summary>
    Unknown,

    /// <summary>File system path — no authentication.</summary>
    Local,

    /// <summary><c>https://…</c> — username + token.</summary>
    Https,

    /// <summary><c>git@host:path</c> or <c>ssh://…</c> — key.</summary>
    Ssh,
}

/// <summary>State of the SSH agent (P06-T09).</summary>
/// <remarks>
/// <b>MEASURED — <c>ssh-add -l</c>'s exit code is a clean diagnostic channel:</b>
/// <c>2</c> no agent · <c>1</c> agent present but empty · <c>0</c> agent present with a key loaded.
/// </remarks>
public enum SshAgentState
{
    /// <summary><c>ssh-add</c> could not be run or returned an unexpected code.</summary>
    Unknown,

    /// <summary>The agent is not running (<c>SSH_AUTH_SOCK</c> is missing).</summary>
    NotRunning,

    /// <summary>The agent is running but has no key loaded.</summary>
    Empty,

    /// <summary>The agent is running and has at least one key loaded.</summary>
    HasKeys,
}

/// <summary>
/// <b>Why</b> authentication failed (P06-T09).
/// </summary>
public sealed record AuthenticationDiagnosis
{
    /// <summary>Connection transport.</summary>
    public required RemoteTransport Transport { get; init; }

    /// <summary>Remote repository URL (password masked).</summary>
    public string? Url { get; init; }

    /// <summary>Does the user have a <c>credential.helper</c> configured?</summary>
    public bool HasCredentialHelper { get; init; }

    /// <summary>SSH agent state; <see cref="SshAgentState.Unknown"/> for HTTPS.</summary>
    public SshAgentState Agent { get; init; }

    /// <summary>
    /// Does it make sense to prompt for credentials and <b>retry</b>?
    /// </summary>
    /// <remarks>
    /// Only for HTTPS. Over SSH what's needed isn't a password but a <b>key</b>; a dialog
    /// can't fix that (measurement: adding a key to the agent is a separate task that requires
    /// the user's own key file).
    /// </remarks>
    public bool CanRetryWithCredentials => Transport == RemoteTransport.Https;

    /// <summary>Explanation shown to the user.</summary>
    public required string Explanation { get; init; }

    /// <summary>Actionable suggestions (a command or a step).</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = [];
}

/// <summary>HTTPS credentials obtained from the user (P06-T09).</summary>
/// <param name="Username">Username.</param>
/// <param name="Secret">Password or personal access token.</param>
public sealed record GitCredentials(string Username, string Secret);

/// <summary>Authentication diagnostics (P06-T09).</summary>
public interface IAuthenticationDiagnostics
{
    /// <summary>Says <b>why</b> a failed remote operation failed.</summary>
    Task<AuthenticationDiagnosis> DiagnoseAsync(
        string workingDirectory,
        string? remote,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Authentication diagnostics (P06-T09).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Without diagnostics the message was WRONG.</b> On the SSH side git appends the line
/// <c>Could not read from remote repository.</c> to all authentication and network errors;
/// because the classifier looked at this line first, a <b>missing SSH key</b> was shown to
/// the user as <i>"remote repository not found"</i>. The classification order was fixed; this
/// class also says <b>what to do</b>.
/// </para>
/// <para>
/// Diagnosis looks at the <b>environment</b>, not git's text: the format of the remote URL,
/// the user's <c>credential.helper</c> setting, and <c>ssh-add -l</c>'s exit code.
/// </para>
/// </remarks>
public sealed class AuthenticationDiagnostics : IAuthenticationDiagnostics
{
    private readonly IGitProcessRunner _runner;
    private readonly ISshAgentProbe _agent;

    public AuthenticationDiagnostics(IGitProcessRunner runner, ISshAgentProbe? agent = null)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
        _agent = agent ?? new SshAgentProbe();
    }

    public async Task<AuthenticationDiagnosis> DiagnoseAsync(
        string workingDirectory,
        string? remote,
        CancellationToken cancellationToken = default)
    {
        string? url = await ReadUrlAsync(workingDirectory, remote, cancellationToken).ConfigureAwait(false);
        RemoteTransport transport = ClassifyTransport(url);
        bool helper = await HasCredentialHelperAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        SshAgentState agent = transport == RemoteTransport.Ssh
            ? await _agent.ProbeAsync(cancellationToken).ConfigureAwait(false)
            : SshAgentState.Unknown;

        return new AuthenticationDiagnosis
        {
            Transport = transport,
            Url = GitRemoteUrl.Mask(url),
            HasCredentialHelper = helper,
            Agent = agent,
            Explanation = Explain(transport, helper, agent),
            Suggestions = Suggest(transport, helper, agent),
        };
    }

    /// <summary>Determines the transport by looking at the shape of the remote URL.</summary>
    /// <remarks>
    /// The <c>git@host:path</c> form has no scheme; addresses with no scheme that contain
    /// <c>:</c> are treated as an SCP shorthand. Windows drive letters (<c>C:\…</c>) are
    /// additionally excluded via a single-letter prefix check so they don't trip this rule.
    /// </remarks>
    internal static RemoteTransport ClassifyTransport(string? url)
    {
        if (url is not { Length: > 0 })
        {
            return RemoteTransport.Unknown;
        }

        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return RemoteTransport.Https;
        }

        if (url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("git+ssh://", StringComparison.OrdinalIgnoreCase))
        {
            return RemoteTransport.Ssh;
        }

        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith('/')
            || url.StartsWith('.')
            || url.StartsWith('~'))
        {
            return RemoteTransport.Local;
        }

        int colon = url.IndexOf(':', StringComparison.Ordinal);

        if (colon > 1 && !url.AsSpan(0, colon).Contains('/'))
        {
            return RemoteTransport.Ssh;
        }

        return url.Contains("://", StringComparison.Ordinal)
            ? RemoteTransport.Unknown
            : RemoteTransport.Local;
    }

    private async Task<string?> ReadUrlAsync(
        string workingDirectory,
        string? remote,
        CancellationToken cancellationToken)
    {
        if (remote is not { Length: > 0 })
        {
            return null;
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "remote", "get-url", "--", remote),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? result.GetStandardOutputText().Trim() : null;
    }

    private async Task<bool> HasCredentialHelperAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // `-z`: the values are separated by NUL, so an EMPTY value can be told apart from a line
        // break. With `\n` separation the empty value is invisible — and it is the one that decides.
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "config", "-z", "--get-all", "credential.helper"),
            cancellationToken).ConfigureAwait(false);

        // MEASURED: if the setting is absent, the exit code is 1 and the output is empty.
        if (!result.IsSuccess)
        {
            return false;
        }

        // 🔴 MEASURED — the ORDER decides, not the presence of a non-empty value. git accumulates
        // the helpers in configuration order and an EMPTY value WIPES the list collected so far
        // (that is how an inherited helper is cancelled). Verified with `git credential fill`:
        //   helper=osxkeychain, helper=""  → git asks the terminal, NO helper runs
        //   helper="", helper=osxkeychain  → the helper runs
        // The old check ("is there any non-empty line") reported the first case as "there is a
        // helper" — on macOS, where the system configuration brings in `osxkeychain`, that meant
        // telling every user their cancelled helper was still active.
        bool helper = false;

        foreach (string value in SplitNulSeparated(result.GetStandardOutputText()))
        {
            helper = value.Trim().Length > 0;
        }

        return helper;
    }

    /// <summary>
    /// Splits <c>git config -z</c> output into values.
    /// </summary>
    /// <remarks>
    /// Each value is <b>terminated</b> by a NUL, so the split leaves one empty element at the very
    /// end; that one is the terminator, not a value. Empty elements before it are real empty values
    /// and must be kept.
    /// </remarks>
    private static IEnumerable<string> SplitNulSeparated(string text)
    {
        string[] parts = text.Split('\0');

        return parts.Length > 0 && parts[^1].Length == 0 ? parts[..^1] : parts;
    }

    private static string Explain(RemoteTransport transport, bool helper, SshAgentState agent) =>
        transport switch
        {
            RemoteTransport.Ssh => agent switch
            {
                SshAgentState.NotRunning =>
                    "The remote uses SSH but there is no SSH agent running in this session, "
                    + "so no key could be offered to the server.",
                SshAgentState.Empty =>
                    "The SSH agent is running but holds no keys.",
                SshAgentState.HasKeys =>
                    "The SSH agent holds keys but the server did not accept them — the key is "
                    + "probably not authorised for this repository.",
                _ =>
                    "The remote uses SSH and the server did not accept the key.",
            },
            RemoteTransport.Https when helper =>
                "The stored credential was rejected. The token may have expired or may not be authorised "
                + "yetmiyor olabilir.",
            RemoteTransport.Https =>
                "The remote requires authentication and there is no stored credential.",
            RemoteTransport.Local =>
                "A local path was unreachable; this is unrelated to authentication.",
            _ =>
                "Authentication failed.",
        };

    private static IReadOnlyList<string> Suggest(
        RemoteTransport transport,
        bool helper,
        SshAgentState agent) => transport switch
        {
            RemoteTransport.Ssh => agent switch
            {
                SshAgentState.NotRunning =>
                [
                    "eval \"$(ssh-agent -s)\"",
                    "ssh-add ~/.ssh/id_ed25519",
                ],
                SshAgentState.Empty => ["ssh-add ~/.ssh/id_ed25519"],
                SshAgentState.HasKeys =>
                [
                    "ssh -T git@<server>",
                    "ssh-add -l",
                ],
                _ => ["ssh-add -l"],
            },

            // ⚠️ `store` is deliberately not suggested: it writes the password to disk in plain text.
            RemoteTransport.Https when !helper =>
            [
                "git config --global credential.helper libsecret",
                "git config --global credential.helper cache",
            ],
            RemoteTransport.Https => ["git credential reject"],
            _ => [],
        };
}

/// <summary>The side that probes the SSH agent (P06-T09).</summary>
/// <remarks>A separate interface: the agent lives outside the process and needs to be mocked in tests.</remarks>
public interface ISshAgentProbe
{
    Task<SshAgentState> ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads agent state via <c>ssh-add -l</c> (P06-T09).
/// </summary>
public sealed class SshAgentProbe : ISshAgentProbe
{
    public async Task<SshAgentState> ProbeAsync(CancellationToken cancellationToken = default)
    {
        // No point starting the process at all if `SSH_AUTH_SOCK` is missing.
        if (System.Environment.GetEnvironmentVariable("SSH_AUTH_SOCK") is not { Length: > 0 })
        {
            return SshAgentState.NotRunning;
        }

        try
        {
            using System.Diagnostics.Process process = new()
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ssh-add",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            process.StartInfo.ArgumentList.Add("-l");

            if (!process.Start())
            {
                return SshAgentState.Unknown;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // MEASURED: 2 no agent · 1 agent empty · 0 key present.
            return process.ExitCode switch
            {
                0 => SshAgentState.HasKeys,
                1 => SshAgentState.Empty,
                2 => SshAgentState.NotRunning,
                _ => SshAgentState.Unknown,
            };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // `ssh-add` is not installed.
            return SshAgentState.Unknown;
        }
    }
}

/// <summary>Remote URL helpers.</summary>
internal static class GitRemoteUrl
{
    /// <summary>Masks the password in the URL.</summary>
    internal static string? Mask(string? url) => Model.GitRemote.MaskCredentials(url);
}
