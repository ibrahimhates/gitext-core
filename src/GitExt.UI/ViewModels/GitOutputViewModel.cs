using GitExt.Core;
using GitExt.Core.Git;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The ViewModel of the component that shows a git command's <b>full output</b> to the user (P05-T07).
/// </summary>
/// <remarks>
/// <para>
/// The reason ADR-0002 chose the <c>git</c> CLI was hook support. It is not enough that a hook merely
/// <b>runs</b>: if the validation the user set up has something to say, that must be visible. Until
/// now the UI showed only the classified summary (<c>"The git command failed."</c>); the hook's actual
/// output stayed inside <see cref="GitException.StandardError"/> and was <b>never shown</b>.
/// </para>
/// <para>
/// The component is deliberately <b>standalone</b>: the commit panel (P05-T12), file operations
/// (P05-T08) and the confirmation flow (P05-T15) will all use the same view.
/// </para>
/// </remarks>
public sealed class GitOutputViewModel : ViewModelBase
{
    private GitOutputViewModel(string title, string summary)
    {
        Title = title;
        Summary = summary;
    }

    /// <summary>The window/section title.</summary>
    public string Title { get; private init; }

    /// <summary>A one-sentence summary — the classified error description, or the status.</summary>
    public string Summary { get; private init; }

    /// <summary>The command that was run, so the user can copy it into their terminal.</summary>
    public string CommandLine { get; private init; } = string.Empty;

    /// <summary>Should <see cref="CommandLine"/> be shown?</summary>
    public bool HasCommandLine => CommandLine.Length > 0;

    /// <summary>
    /// git's exit code; <see langword="null"/> on the success path.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is <b>git's</b> exit code, not the hook's. Measured (P05-T07): with a
    /// <c>pre-commit</c> hook exiting 3, git still returns <b>1</b> — the hook's code is lost. The UI
    /// cannot present this number as "the hook exited with 3".
    /// </remarks>
    public int? ExitCode { get; private init; }

    /// <summary>Should the exit code line be shown?</summary>
    public bool HasExitCode => ExitCode is not null;

    /// <summary>The display text for the exit code.</summary>
    public string ExitCodeText => ExitCode is { } code ? $"Exit code: {code}" : string.Empty;

    /// <summary>
    /// The command's full output — prepared for display (ANSI codes stripped, <c>\r</c> applied).
    /// </summary>
    public string Output { get; private init; } = string.Empty;

    /// <summary>Is there any output to show?</summary>
    public bool HasOutput => Output.Length > 0;

    /// <summary>When the output was truncated, a note saying how many lines were dropped.</summary>
    public string TruncationNotice { get; private init; } = string.Empty;

    /// <summary>Should the truncation note be shown?</summary>
    public bool HasTruncationNotice => TruncationNotice.Length > 0;

    /// <summary>
    /// When a hook changed the message, the final message that went into the commit; empty otherwise.
    /// </summary>
    public string FinalMessage { get; private init; } = string.Empty;

    /// <summary>Should the final message section be shown?</summary>
    public bool HasFinalMessage => FinalMessage.Length > 0;

    /// <summary>
    /// Prepares a failed git command for display.
    /// </summary>
    /// <param name="exception">The exception caught.</param>
    /// <param name="title">The title; a generic one is used when it is not given.</param>
    public static GitOutputViewModel ForFailure(GitException exception, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string output = GitOutputText.CleanForDisplay(exception.StandardError, out int dropped);

        return new GitOutputViewModel(title ?? Loc.T("git_output.the_git_command_failed"), exception.Message)
        {
            CommandLine = exception.CommandLine,
            ExitCode = exception.ExitCode,
            Output = output,
            TruncationNotice = Notice(dropped),
        };
    }

    /// <summary>
    /// Prepares a completed commit for display.
    /// </summary>
    /// <remarks>
    /// <b>This is NOT where the decision to show it is made</b> — the caller decides with
    /// <see cref="CommitResult.NeedsReporting"/>. Empty content is noise in a separate window, but in
    /// a section embedded in the commit panel (P05-T12) "no hook output" is a perfectly good answer;
    /// the side that knows the surface should make the call.
    /// </remarks>
    public static GitOutputViewModel ForCommit(CommitResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        string output = GitOutputText.CleanForDisplay(result.Output, out int dropped);
        bool messageChanged = result.MessageChanged;

        string summary = messageChanged
            ? Loc.T("git_output.commit_created_hooks_changed_the_commit_mess")
            : Loc.T("git_output.commit_created");

        return new GitOutputViewModel(Loc.T("git_output.commit_complete"), summary)
        {
            Output = output,
            TruncationNotice = Notice(dropped),

            // Not shown when it did not change: reading back the text the user already wrote is noise,
            // not information.
            FinalMessage = messageChanged ? result.Message : string.Empty,
        };
    }

    private static string Notice(int droppedLines) =>
        droppedLines > 0
            ? $"The output is too long; the first {droppedLines} lines are hidden (last "
              + $"{GitOutputText.MaximumDisplayLines} lines below)."
            : string.Empty;
}
