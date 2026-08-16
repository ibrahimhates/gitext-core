using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// An external merge tool (P07-T04).
/// </summary>
public sealed record MergeTool
{
    /// <summary>The name git knows it by — <c>meld</c>, <c>kdiff3</c>, <c>vscode</c>…</summary>
    public required string Name { get; init; }

    /// <summary>The description git prints.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Is the tool <b>installed</b> on this machine?
    /// </summary>
    /// <remarks>
    /// <c>git mergetool --tool-help</c> prints two lists: the ones that can be used and the ones that
    /// are <i>"valid, but not currently available"</i>. Letting the user pick one that is not
    /// installed would have them clicking a button that does not work.
    /// </remarks>
    public bool IsAvailable { get; init; }
}

/// <summary>External merge tool integration (P07-T04).</summary>
public interface IMergeToolRunner
{
    /// <summary>The user's <c>merge.tool</c> setting; <see langword="null"/> when absent.</summary>
    Task<string?> GetConfiguredToolAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the tools git knows about.</summary>
    Task<IReadOnlyList<MergeTool>> ListToolsAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the external tool for a conflicting file.
    /// </summary>
    /// <param name="workingDirectory">The repository's working directory.</param>
    /// <param name="path">The conflicting file; when <see langword="null"/>, all conflicting ones.</param>
    /// <param name="tool">The tool to use; when <see langword="null"/>, the configured one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<MergeToolResult> RunAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        string? tool = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The external tool's result (P07-T04).</summary>
public sealed record MergeToolResult
{
    /// <summary>Did the file count as resolved after the tool ran?</summary>
    public required bool IsResolved { get; init; }

    /// <summary>The output the tool left behind (to be shown to the user).</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// The <c>.orig</c> backups the tool left behind.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>MEASURED — <c>git mergetool</c> leaves a <c>&lt;name&gt;.orig</c> for every file</b> and
    /// they stay in the working tree as untracked files. A user not expecting this asks "where did
    /// these files come from"; they are listed and their deletion is offered.
    /// (The <c>mergetool.keepBackup=false</c> setting also turns this off, but <b>we</b> do not change
    /// the user's configuration.)
    /// </remarks>
    public IReadOnlyList<RepositoryPath> BackupFiles { get; init; } = [];
}

/// <summary>
/// The <c>git mergetool</c> wrapper (P07-T04).
/// </summary>
/// <remarks>
/// <para>
/// The reasoning from the plan: <i>"Supporting the tool the user has already installed pays off more
/// than trying to perfect the built-in view."</i> The built-in three-way view (P07-T03) is for simple
/// conflicts, the external tool for complex ones.
/// </para>
/// <para>
/// <c>--no-prompt</c> is passed: <c>git mergetool</c> normally reads from stdin for every file with
/// <i>"Hit return to start merge resolution tool"</i>.
/// ℹ️ <b>MEASURED — this does not deadlock in our case:</b> with stdin closed, git reads EOF and
/// carries on (rc=0, 0 s). So the flag is not a <i>fix</i> but a choice not to depend on that
/// behaviour — and it keeps the needless prompt out of the output.
/// </para>
/// </remarks>
public sealed class MergeToolRunner : IMergeToolRunner
{
    private const string UnavailableMarker = "not currently available";

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public MergeToolRunner(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<string?> GetConfiguredToolAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", "--get", "merge.tool"],

                // With the setting absent, `git config --get` gives exit code 1; that is not an error.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        string value = result.GetStandardOutputText().Trim();
        return value.Length == 0 ? null : value;
    }

    public async Task<IReadOnlyList<MergeTool>> ListToolsAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "mergetool", "--tool-help"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? ParseToolHelp(result.GetStandardOutputText()) : [];
    }

    /// <summary>
    /// Parses the <c>git mergetool --tool-help</c> output.
    /// </summary>
    /// <remarks>
    /// The output has two sections: first the usable ones, then whatever follows the
    /// <i>"valid, but not currently available"</i> heading. Tool lines are indented with a tab and
    /// take the form <c>&lt;name&gt;&lt;spaces&gt;&lt;description&gt;</c>.
    /// </remarks>
    internal static IReadOnlyList<MergeTool> ParseToolHelp(string output)
    {
        List<MergeTool> tools = [];
        bool available = true;

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.Contains(UnavailableMarker, StringComparison.Ordinal))
            {
                available = false;
                continue;
            }

            // Tool lines are indented; headings are not.
            if (line.Length == 0 || (line[0] != '\t' && line[0] != ' '))
            {
                continue;
            }

            string trimmed = line.Trim();
            int space = trimmed.IndexOf(' ', StringComparison.Ordinal);

            if (trimmed.Length == 0)
            {
                continue;
            }

            string name = space < 0 ? trimmed : trimmed[..space];

            tools.Add(new MergeTool
            {
                Name = name,
                Description = space < 0 ? string.Empty : trimmed[space..].Trim(),
                IsAvailable = available,
            });
        }

        return tools;
    }

    public async Task<MergeToolResult> RunAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        string? tool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        List<string> arguments = ["mergetool", "--no-prompt"];

        if (tool is { Length: > 0 } name)
        {
            arguments.Add($"--tool={name}");
        }

        if (path is { } target && !target.IsEmpty)
        {
            arguments.Add("--");
            arguments.Add(target.Value);
        }

        GitResult result = await _writer
            .RunAsync(workingDirectory, arguments, cancellationToken)
            .ConfigureAwait(false);

        GitResult conflicts = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        // The INDEX's state decides, not the tool's exit code: some tools return 0 even when the user
        // closes them without saving (`trustExitCode` is off by default).
        bool resolved = conflicts.IsSuccess
            && conflicts.GetStandardOutputText().Split('\0', StringSplitOptions.RemoveEmptyEntries).Length == 0;

        return new MergeToolResult
        {
            IsResolved = resolved,
            Output = result.GetStandardOutputText(),
            BackupFiles = await FindBackupsAsync(workingDirectory, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Finds the <c>.orig</c> files the tool left behind.</summary>
    private async Task<IReadOnlyList<RepositoryPath>> FindBackupsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory,
                "ls-files", "--others", "--exclude-standard", "-z", "--", "*.orig"),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return [];
        }

        List<RepositoryPath> backups = [];

        foreach (string value in result.GetStandardOutputText()
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (RepositoryPath.TryParse(value, out RepositoryPath path))
            {
                backups.Add(path);
            }
        }

        return backups;
    }
}
