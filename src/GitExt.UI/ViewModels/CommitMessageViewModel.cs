using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.UI.Localization;

namespace GitExt.UI.ViewModels;

/// <summary>
/// A single history entry in the "Message ▾" menu (P05-T13).
/// </summary>
public sealed class CommitMessageHistoryItem
{
    /// <summary>Upper bound of the label shown in the menu.</summary>
    /// <remarks>
    /// 72 in GitExtensions too (<c>maxLabelLength</c>). A menu item must be a single line;
    /// a multi-line message would push the menu off screen.
    /// </remarks>
    public const int LabelLimit = 72;

    public CommitMessageHistoryItem(string message, ICommand applyCommand)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(applyCommand);

        Message = message;
        ApplyCommand = applyCommand;

        int newline = message.IndexOf('\n', StringComparison.Ordinal);
        string firstLine = (newline < 0 ? message : message[..newline]).TrimEnd('\r');

        Label = firstLine.Length > LabelLimit
            ? string.Concat(firstLine.AsSpan(0, LabelLimit - 1), "…")
            : firstLine;
    }

    /// <summary>The full message — this is what goes into the box when selected.</summary>
    public string Message { get; }

    /// <summary>Single-line label shown in the menu.</summary>
    public string Label { get; }

    public ICommand ApplyCommand { get; }

    public override string ToString() => Label;
}

/// <summary>
/// State of the commit message box (P05-T12) and its helpers (P05-T13).
/// </summary>
/// <remarks>
/// <para>
/// Subject line ≤ <b>50</b>, body lines ≤ <b>72</b> — the git community's established
/// convention (<c>git log --oneline</c> and email patches are formatted for these widths).
/// </para>
/// <para>
/// <b>The GitExtensions equivalent was measured:</b> it too has limits
/// (<c>CommitValidationMaxCntCharsFirstLine</c>, <c>…PerLine</c>,
/// <c>CommitValidationSecondLineMustBeEmpty</c>) but <b>their defaults are 0 / off</b> and
/// the check appears as a confirmation dialog <i>at</i> commit time. The opposite was chosen
/// here: the limit is shown <b>while typing</b>, it blocks nothing. Saying "fix this" after
/// the message is written and finished would mean forcing the user to undo work they already did.
/// </para>
/// <para>
/// ⚠️ <b>No limit BLOCKS the commit.</b> A long subject line can be a deliberate choice; it's
/// not right for the app to restrict the user in their own repository.
/// </para>
/// <para>
/// 🔑 <b>P05-T13's invariant rule: no source ever overwrites text the user has typed.</b>
/// The draft, the template, <c>MERGE_MSG</c> and the <c>--amend</c> message are only loaded
/// while the box is <b>empty</b>. Picking a message from history is the one exception —
/// there the user explicitly wants to replace it (same as GitExtensions'
/// <c>ReplaceMessage</c>).
/// </para>
/// </remarks>
public sealed partial class CommitMessageViewModel : ViewModelBase
{
    /// <summary>Suggested upper bound for the subject line.</summary>
    public const int SubjectLimit = 50;

    /// <summary>Suggested upper bound for body lines.</summary>
    public const int BodyLimit = 72;

    /// <summary>Maximum number of history messages to show in the menu.</summary>
    /// <remarks>GitExtensions' default is also 6 (<c>CommitDialogNumberOfPreviousMessages</c>).</remarks>
    public const int HistoryCount = 6;

    private readonly ICommitMessageReader? _reader;
    private readonly ICommitMessageStore? _store;

    private string? _workingDirectory;

    /// <summary>Text changes during loading must not trigger a draft save.</summary>
    private bool _loading;

    private CancellationTokenSource? _draftSave;

    public CommitMessageViewModel(
        ICommitMessageReader? reader = null,
        ICommitMessageStore? store = null)
    {
        _reader = reader;
        _store = store;

        ApplyHistoryCommand = new RelayCommand<CommitMessageHistoryItem>(item =>
        {
            if (item is not null)
            {
                Text = item.Message;
            }
        });
    }

    /// <summary>Columns for the guide lines.</summary>
    public static IReadOnlyList<int> GuideColumns { get; } = [SubjectLimit, BodyLimit];

    /// <summary>Instance-based access for XAML binding.</summary>
    /// <remarks>
    /// Avalonia binding can't see a <c>static</c> member via <c>{Binding}</c>; writing the guide
    /// columns as a fixed array into XAML instead would mean the limits live in two places.
    /// </remarks>
    public IReadOnlyList<int> GuideColumnsForBinding => GuideColumns;

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(SubjectLength));
        OnPropertyChanged(nameof(SubjectCounter));
        OnPropertyChanged(nameof(IsSubjectTooLong));
        OnPropertyChanged(nameof(HasNonEmptySecondLine));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(HasHint));

        ScheduleDraftSave();
    }

    /// <summary>Is the message empty? (whitespace-only also counts as empty)</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    /// <summary>First line — the commit's subject.</summary>
    private string Subject
    {
        get
        {
            int newline = Text.IndexOf('\n', StringComparison.Ordinal);

            return (newline < 0 ? Text : Text[..newline]).TrimEnd('\r');
        }
    }

    public int SubjectLength => Subject.Length;

    /// <summary>Counter text; the user sees this while typing near the limit.</summary>
    public string SubjectCounter => $"{SubjectLength} / {SubjectLimit}";

    public bool IsSubjectTooLong => SubjectLength > SubjectLimit;

    /// <summary>
    /// Is the second line non-empty? (there should be a blank line between subject and body)
    /// </summary>
    /// <remarks>
    /// git treats this distinction as <b>meaningful</b>: <c>%s</c> gives the first line, <c>%b</c>
    /// gives everything after the blank line. If the second line is non-empty, the body sticks
    /// to the subject and <c>git log</c> output looks broken.
    /// </remarks>
    public bool HasNonEmptySecondLine
    {
        get
        {
            string[] lines = Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            return lines.Length > 2 && lines[1].Trim().Length > 0;
        }
    }

    /// <summary>Formatting suggestion shown to the user; empty if there's no issue.</summary>
    public string Hint => this switch
    {
        { HasNonEmptySecondLine: true } => Loc.T("commit_message.leave_a_blank_line_between_the_subject_and_t"),
        { IsSubjectTooLong: true } => $"The subject line exceeds {SubjectLimit} characters.",
        _ => string.Empty,
    };

    public bool HasHint => Hint.Length > 0;

    // ---- History (P05-T13) ----

    /// <summary>Contents of the "Message ▾" menu.</summary>
    public ObservableCollection<CommitMessageHistoryItem> RecentMessages { get; } = [];

    /// <summary>
    /// List only messages from the user's own commits?
    /// </summary>
    /// <remarks>
    /// Corresponds to GitExtensions' <c>ShowOnlyMyMessages</c>. In a shared repository, the
    /// last six commits could all belong to someone else, making the menu useless.
    /// </remarks>
    [ObservableProperty]
    public partial bool OnlyMyMessages { get; set; }

    partial void OnOnlyMyMessagesChanged(bool value) => _ = LoadRecentAsync();

    /// <summary>
    /// Reads past messages.
    /// </summary>
    /// <remarks>
    /// Called when the menu <b>opens</b>, not when the repository opens: running one more
    /// <c>git log</c> on every repository open would be a cost paid for a menu the user might
    /// never open.
    /// </remarks>
    public async Task LoadRecentAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        IReadOnlyList<string> messages;

        try
        {
            messages = await _reader
                .ReadRecentAsync(directory, HistoryCount, OnlyMyMessages, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Core.Git.GitException)
        {
            // If history can't be read, the menu stays empty; the commit screen must keep working.
            return;
        }

        RecentMessages.Clear();

        foreach (string message in messages)
        {
            RecentMessages.Add(new CommitMessageHistoryItem(message, ApplyHistoryCommand));
        }
    }

    /// <summary>Puts the selected history message into the box.</summary>
    /// <remarks>
    /// This is the <b>one</b> place where existing text is overwritten — the user asked for
    /// exactly that by selecting from the menu (same as GitExtensions' <c>ReplaceMessage</c>).
    /// </remarks>
    public IRelayCommand<CommitMessageHistoryItem> ApplyHistoryCommand { get; }

    // ---- Template (P05-T13) ----

    /// <summary>Template configured via <c>commit.template</c>; <see langword="null"/> if there is none.</summary>
    [ObservableProperty]
    public partial CommitTemplate? Template { get; private set; }

    partial void OnTemplateChanged(CommitTemplate? value)
    {
        OnPropertyChanged(nameof(HasTemplate));
        OnPropertyChanged(nameof(CanApplyTemplate));
        OnPropertyChanged(nameof(TemplateLabel));
    }

    /// <summary>Is a template configured for the repository? (the file may be missing)</summary>
    public bool HasTemplate => Template is not null;

    /// <summary>Can the template actually be applied?</summary>
    public bool CanApplyTemplate => Template is { IsMissing: false };

    /// <summary>
    /// Template line shown in the menu.
    /// </summary>
    /// <remarks>
    /// A missing template is <b>not hidden</b>: in that situation git itself rejects the commit
    /// with <c>fatal: could not read</c> (measured), meaning the user's configuration is
    /// genuinely broken. Showing an empty menu would hide the problem.
    /// </remarks>
    public string TemplateLabel => Template switch
    {
        null => Loc.T("commit_message.commit_template_is_not_set"),
        { IsMissing: true } t => $"Template not found: {t.Path}",
        { Path: var path } => Path.GetFileName(path),
    };

    /// <summary>
    /// Loads the template into the box.
    /// </summary>
    /// <remarks>
    /// 🔴 Comment lines are loaded <b>stripped</b> (<see cref="CommitMessageText"/>): git's
    /// editor path doesn't let them into the commit, but our <c>--cleanup=whitespace</c> path
    /// would. Whatever appears in the box is what gets committed.
    /// </remarks>
    public async Task ApplyTemplateAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        if (Template is not { Text: { } text })
        {
            return;
        }

        string commentCharacter = await _reader
            .ReadCommentCharacterAsync(directory, cancellationToken)
            .ConfigureAwait(true);

        SetLoadedText(CommitMessageText.PrepareForEditing(text, commentCharacter));
    }

    // ---- Draft and loading (P05-T13) ----

    /// <summary>
    /// Time to wait before the draft is written to disk.
    /// </summary>
    /// <remarks>
    /// Instead of writing to the file on every keystroke, it's written once after the last one.
    /// Adjustable so it can be reset in tests.
    /// </remarks>
    public TimeSpan DraftSaveDelay { get; set; } = TimeSpan.FromMilliseconds(750);

    /// <summary>Source of the text loaded into the box; <see cref="CommitMessageSource.None"/> if the user typed it.</summary>
    [ObservableProperty]
    public partial CommitMessageSource Source { get; private set; }

    /// <summary>
    /// Binds to a repository and, if there is a message to load, puts it in the box.
    /// </summary>
    public async Task OpenAsync(string? workingDirectory, CancellationToken cancellationToken = default)
    {
        _workingDirectory = workingDirectory;

        RecentMessages.Clear();
        Template = null;
        Source = CommitMessageSource.None;

        if (workingDirectory is not { Length: > 0 } directory)
        {
            SetLoadedText(string.Empty);
            return;
        }

        if (_reader is not null)
        {
            try
            {
                Template = await _reader.ReadTemplateAsync(directory, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Core.Git.GitException)
            {
                Template = null;
            }
        }

        if (_store is null)
        {
            return;
        }

        PendingCommitMessage pending = await _store.ReadAsync(directory, cancellationToken)
            .ConfigureAwait(true);

        // Text the user is currently typing is never overwritten. When the screen is reopened
        // the box is already empty; the only case it's non-empty is when the screen stayed open.
        if (pending.HasText && IsEmpty)
        {
            SetLoadedText(pending.Text);
            Source = pending.Source;
        }
    }

    /// <summary>
    /// Loads <c>HEAD</c>'s message (when <c>--amend</c> is checked).
    /// </summary>
    /// <remarks>
    /// Only while the box is empty. Same condition as GitExtensions: if the user already
    /// started typing a new message, checking the amend box shouldn't mean erasing it.
    /// </remarks>
    public async Task LoadHeadMessageAsync(CancellationToken cancellationToken = default)
    {
        if (_reader is null || !IsEmpty || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        string? message;

        try
        {
            message = await _reader.ReadHeadMessageAsync(directory, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Core.Git.GitException)
        {
            return;
        }

        if (message is { Length: > 0 })
        {
            SetLoadedText(message);
        }
    }

    /// <summary>
    /// Writes a pending draft save to disk immediately.
    /// </summary>
    /// <remarks>
    /// Called while the window is closing: the delayed save might not have run yet and the
    /// user's last-typed line would be lost.
    /// </remarks>
    public async Task FlushDraftAsync(CancellationToken cancellationToken = default)
    {
        CancelPendingSave();

        if (_store is null || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        await _store.SaveDraftAsync(directory, Text, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// After a successful commit: the box and the draft are cleared.
    /// </summary>
    /// <remarks>
    /// Deleting the draft is <b>mandatory</b>: if the committed message stayed on disk, the
    /// text just committed would come back the next time the screen opens and invite a second commit.
    /// </remarks>
    public async Task OnCommittedAsync(CancellationToken cancellationToken = default)
    {
        Clear();

        if (_store is null || _workingDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        await _store.ClearDraftAsync(directory, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Clears the message.</summary>
    public void Clear()
    {
        CancelPendingSave();

        _loading = true;

        try
        {
            Text = string.Empty;
            Source = CommitMessageSource.None;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Puts externally provided text into the box; does not trigger a draft save.</summary>
    private void SetLoadedText(string text)
    {
        _loading = true;

        try
        {
            Text = text;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Defers the draft save.
    /// </summary>
    /// <remarks>
    /// ⚠️ There is <b>no</b> <c>await</c> between cancelling and assigning the new token — this
    /// was measured in P04-T08: when there's an <c>await</c> in between, back-to-back calls
    /// can't cancel each other and each one starts a separate job.
    /// </remarks>
    private void ScheduleDraftSave()
    {
        if (_loading || _store is null || _workingDirectory is not { Length: > 0 })
        {
            return;
        }

        CancelPendingSave();

        _draftSave = new CancellationTokenSource();

        _ = SaveDraftLaterAsync(Text, _draftSave.Token);
    }

    private async Task SaveDraftLaterAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            if (DraftSaveDelay > TimeSpan.Zero)
            {
                await Task.Delay(DraftSaveDelay, cancellationToken).ConfigureAwait(true);
            }

            if (_store is not null && _workingDirectory is { Length: > 0 } directory)
            {
                await _store.SaveDraftAsync(directory, text, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // The user kept typing; the next save is already scheduled.
        }
    }

    private void CancelPendingSave()
    {
        _draftSave?.Cancel();
        _draftSave?.Dispose();
        _draftSave = null;
    }
}
