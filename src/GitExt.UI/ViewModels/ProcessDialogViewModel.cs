using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core.Git;
using GitExt.UI.Localization;
using GitExt.UI.Settings;

namespace GitExt.UI.ViewModels;

/// <summary>Where a process window is in its life cycle.</summary>
public enum ProcessDialogState
{
    /// <summary>Not started yet.</summary>
    Waiting,

    /// <summary>The command is running.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Success,

    /// <summary>Failed, or the user aborted it.</summary>
    Error,
}

/// <summary>
/// The work a process window runs.
/// </summary>
/// <param name="output">Every piece of raw output goes here, as it is produced.</param>
/// <param name="cancellationToken">Cancelled when the user presses <b>Abort</b>.</param>
/// <returns><see langword="true"/> when the operation succeeded.</returns>
public delegate Task<bool> ProcessDialogOperation(
    IProgress<GitOutputChunk> output,
    CancellationToken cancellationToken);

/// <summary>
/// The side that shows a process window — this is what every write operation plugs into.
/// </summary>
/// <remarks>
/// The equivalent of GitExtensions' <c>FormProcess.ShowDialog(owner, …)</c>: it returns
/// <see langword="false"/> when the command failed, so a caller can branch on the result exactly as
/// it does there.
/// </remarks>
public interface IProcessDialogPrompt
{
    /// <summary>Shows the window and does not return until it closes.</summary>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    Task<bool> ShowAsync(ProcessDialogViewModel model);
}

/// <summary>
/// The process window: what git is printing right now, and what happens when it stops.
/// </summary>
/// <remarks>
/// <para>
/// This is GitExtensions' <c>FormStatus</c> + <c>FormProcess</c> pair. The behaviour it mirrors,
/// read off <c>FormStatus.Done</c> and <c>FormStatus.KeepDialogOpen_CheckedChanged</c>:
/// </para>
/// <list type="bullet">
/// <item>success and <b>Keep dialog open</b> unchecked — the window closes by itself;</item>
/// <item>success and the box checked — it stays, with <b>OK</b> enabled and focused;</item>
/// <item>failure — it stays <b>whatever the box says</b>: the error is the reason it exists;</item>
/// <item><b>Abort</b> — the process is killed, <c>Aborted</c> goes into the log, and the result
/// counts as a failure, so the window stays;</item>
/// <item>unchecking the box <b>after</b> a successful finish closes the window there and then.</item>
/// </list>
/// <para>
/// ⚠️ Output arrives on a <b>pool thread</b>. Everything that touches an observable property is
/// funnelled through the <see cref="Dispatcher"/>; writing to the buffer from the reader thread
/// would corrupt it silently rather than crash.
/// </para>
/// </remarks>
public sealed partial class ProcessDialogViewModel : ViewModelBase, IDisposable
{
    private readonly ProcessDialogOperation _operation;
    private readonly ISettingsStore? _settings;
    private readonly GitConsoleBuffer _console;
    private readonly IProgress<GitOutputChunk> _sink;

    private CancellationTokenSource? _cancellation;
    private ProcessDialogState _state = ProcessDialogState.Waiting;
    private string _output = string.Empty;
    private string _statusLine = string.Empty;
    private double _progressPercent;
    private bool _isProgressIndeterminate = true;
    private bool _keepDialogOpen;
    private bool _disposed;

    public ProcessDialogViewModel(ProcessDialogRequest request, ISettingsStore? settings = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        _operation = request.Operation;
        _settings = settings;
        _console = new GitConsoleBuffer();

        BaseTitle = request.Title;
        CommandLine = request.CommandLine;
        UseDialogSettings = request.UseDialogSettings;
        CanAbort = request.CanAbort;

        // GitExtensions stores the INVERSE (`closeprocessdialog`), and its default is `false` —
        // so out of the box the window stays open. Mirrored rather than "improved": a user coming
        // from GitExtensions must not find their push window vanishing.
        _keepDialogOpen = !(settings?.Current.General.CloseProcessDialog ?? false);

        // NOT `Progress<T>`: that posts to the context captured at construction, and
        // `Apply` already marshals. Two hops would reorder chunks against each other.
        _sink = new OutputSink(Apply);

        StartCommand = new AsyncRelayCommand(StartAsync, () => State is ProcessDialogState.Waiting);
        AbortCommand = new RelayCommand(Abort, () => IsRunning && CanAbort);
        OkCommand = new RelayCommand(RequestClose, () => IsFinished);
    }

    /// <summary>Raised when the window should close — the <b>only</b> way this type closes it.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The window title without the live status line.</summary>
    public string BaseTitle { get; }

    /// <summary>The command being run, for the user to copy into a terminal.</summary>
    public string CommandLine { get; }

    /// <summary>
    /// Is this window allowed to close itself on success?
    /// </summary>
    /// <remarks>
    /// GitExtensions' <c>useDialogSettings</c>. When <see langword="false"/> the <b>Keep dialog
    /// open</b> box is hidden and the window never closes on its own — the caller wants the output
    /// seen no matter what.
    /// </remarks>
    public bool UseDialogSettings { get; }

    /// <summary>Does this operation support being aborted?</summary>
    /// <remarks>
    /// GitExtensions hides the <b>Abort</b> button when no <c>AbortCallback</c> was supplied.
    /// </remarks>
    public bool CanAbort { get; }

    /// <summary>
    /// The title: while running, the last progress line — as <c>FormStatus.SetProgressAsync</c> does.
    /// </summary>
    public string Title => _statusLine.Length > 0 ? _statusLine : BaseTitle;

    public ProcessDialogState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsFinished));
            OnPropertyChanged(nameof(HasFailed));
            OnPropertyChanged(nameof(IsProgressVisible));
            OnPropertyChanged(nameof(CanClose));
            AbortCommand.NotifyCanExecuteChanged();
            OkCommand.NotifyCanExecuteChanged();
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsRunning => State is ProcessDialogState.Running;

    public bool IsFinished => State is ProcessDialogState.Success or ProcessDialogState.Error;

    public bool HasFailed => State is ProcessDialogState.Error;

    /// <summary>
    /// Can the window be closed with its own close button?
    /// </summary>
    /// <remarks>
    /// <c>FormStatus</c> takes the X away entirely while a command runs
    /// (<c>CP_NOCLOSE_BUTTON</c>): closing the window would leave a git process running with
    /// nothing watching it, and an <c>index.lock</c> behind it.
    /// </remarks>
    public bool CanClose => !IsRunning;

    /// <summary>The console body — everything git has written so far.</summary>
    public string Output
    {
        get => _output;
        private set => SetProperty(ref _output, value);
    }

    public bool HasOutput => Output.Length > 0;

    /// <summary>Should the progress bar be on screen?</summary>
    public bool IsProgressVisible => IsRunning;

    /// <summary>0–100; meaningful only while <see cref="IsProgressIndeterminate"/> is false.</summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    /// <summary>
    /// True until git reports a percentage — the bar starts as a marquee, as it does in
    /// <c>FormStatus</c> (<c>ProgressBarStyle.Marquee</c> → <c>Blocks</c>).
    /// </summary>
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    /// <summary>
    /// Keep the window open when the command succeeds.
    /// </summary>
    /// <remarks>
    /// Setting it writes straight through to the settings, the way the checkbox does in
    /// GitExtensions; and turning it <b>off</b> once the command has already succeeded closes the
    /// window immediately, so the checkbox means the same thing before and after the fact.
    /// </remarks>
    public bool KeepDialogOpen
    {
        get => _keepDialogOpen;
        set
        {
            if (!SetProperty(ref _keepDialogOpen, value))
            {
                return;
            }

            _settings?.Update(settings => settings.General.CloseProcessDialog = !value);

            if (!value && State is ProcessDialogState.Success)
            {
                RequestClose();
            }
        }
    }

    public IAsyncRelayCommand StartCommand { get; }

    public IRelayCommand AbortCommand { get; }

    public IRelayCommand OkCommand { get; }

    /// <summary>
    /// Runs the operation. Safe to call again — <c>FormStatus.Retry</c> exists for exactly the
    /// case where a remote operation is repeated after the credentials are fixed.
    /// </summary>
    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        Reset();

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();

        State = ProcessDialogState.Running;

        bool success;

        try
        {
            success = await _operation(_sink, _cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            success = false;
        }
        catch (GitException error)
        {
            AppendLine(error.StandardError);
            AppendLine(Loc.GitError(error));
            success = false;
        }
        catch (Exception error)
        {
            AppendLine(error.Message);
            success = false;
        }

        Done(success);
    }

    /// <summary>Repeats the operation after a failure, as <c>FormStatus.Retry</c> does.</summary>
    public Task RetryAsync()
    {
        State = ProcessDialogState.Waiting;
        return StartAsync();
    }

    /// <summary>
    /// Marks the operation as finished and applies the close rule.
    /// </summary>
    /// <remarks>The counterpart of <c>FormStatus.Done(bool isSuccess)</c>.</remarks>
    public void Done(bool isSuccess)
    {
        State = isSuccess ? ProcessDialogState.Success : ProcessDialogState.Error;
        _statusLine = string.Empty;
        OnPropertyChanged(nameof(Title));

        if (isSuccess && UseDialogSettings && !KeepDialogOpen)
        {
            RequestClose();
        }
    }

    private sealed class OutputSink(Action<GitOutputChunk> handler) : IProgress<GitOutputChunk>
    {
        public void Report(GitOutputChunk value) => handler(value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private void Abort()
    {
        _cancellation?.Cancel();

        // The literal text matters: `FormProcess.IsOperationAborted` recognises an aborted
        // operation by this word in the output.
        AppendLine("Aborted");
        Done(isSuccess: false);
    }

    private void Reset()
    {
        _console.Clear();
        _statusLine = string.Empty;
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        Output = string.Empty;
        OnPropertyChanged(nameof(HasOutput));
        OnPropertyChanged(nameof(Title));
    }

    private void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void AppendLine(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Apply(new GitOutputChunk(Environment.NewLine + text, IsError: true));
    }

    private void Apply(GitOutputChunk chunk)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Apply(chunk));
            return;
        }

        _console.Append(chunk.Text);

        Output = _console.GetText();
        OnPropertyChanged(nameof(HasOutput));

        TrackProgress(chunk.Text);
    }

    private void TrackProgress(string text)
    {
        (IReadOnlyList<string> lines, string remainder) = GitProgressParser.SplitLines(text);

        foreach (string line in lines)
        {
            Observe(line);
        }

        Observe(remainder);
    }

    private void Observe(string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        if (GitProgressParser.Parse(line) is not { } step)
        {
            return;
        }

        if (step.Percent is { } percent)
        {
            ProgressPercent = Math.Clamp(percent, 0, 100);
            IsProgressIndeterminate = false;
        }

        _statusLine = step.Describe();
        OnPropertyChanged(nameof(Title));
    }
}

/// <summary>
/// What a process window is asked to run.
/// </summary>
/// <remarks>
/// The equivalent of the arguments <c>FormProcess.ShowDialog</c> takes. Anything that wants a
/// process window builds one of these and hands it to <see cref="IProcessDialogPrompt"/>.
/// </remarks>
public sealed record ProcessDialogRequest
{
    /// <summary>The window title — <c>Process</c> in GitExtensions, plus the repository path.</summary>
    public required string Title { get; init; }

    /// <summary>The operation to run.</summary>
    public required ProcessDialogOperation Operation { get; init; }

    /// <summary>The command line shown to the user; empty hides that row.</summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>May the window close itself when the command succeeds?</summary>
    public bool UseDialogSettings { get; init; } = true;

    /// <summary>Is there anything to abort?</summary>
    public bool CanAbort { get; init; } = true;

    /// <summary>
    /// Builds a request that runs a single <c>git</c> command and streams its output.
    /// </summary>
    /// <remarks>
    /// The direct equivalent of <c>FormProcess.ShowDialog(owner, commands, arguments, …)</c>:
    /// success is the exit code being one of <see cref="GitCommand.SuccessExitCodes"/>.
    /// </remarks>
    public static ProcessDialogRequest ForCommand(
        IGitProcessRunner runner,
        GitCommand command,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(command);

        return new ProcessDialogRequest
        {
            Title = title ?? Loc.T("process.process"),
            CommandLine = command.ToDisplayString(),
            Operation = async (output, cancellationToken) =>
            {
                GitResult result = await runner
                    .RunAsync(command with { OutputReceived = output }, cancellationToken)
                    .ConfigureAwait(false);

                return result.IsSuccess;
            },
        };
    }

    /// <summary>
    /// Builds a request that shows an already-finished failure — <c>FormStatus.ShowErrorDialog</c>.
    /// </summary>
    /// <remarks>
    /// Nothing runs: there is no progress bar and no <b>Abort</b>, and the window never closes
    /// itself, because what it exists to show is the error.
    /// </remarks>
    public static ProcessDialogRequest ForError(string title, string output) =>
        new()
        {
            Title = title,
            UseDialogSettings = false,
            CanAbort = false,
            Operation = (sink, _) =>
            {
                sink.Report(new GitOutputChunk(output ?? string.Empty, IsError: true));
                return Task.FromResult(false);
            },
        };
}
