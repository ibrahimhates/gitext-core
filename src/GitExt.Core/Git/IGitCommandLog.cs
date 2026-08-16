using System.Collections.Concurrent;

namespace GitExt.Core.Git;

/// <summary>
/// The record of every <c>git</c> command that runs.
/// </summary>
/// <remarks>
/// The infrastructure behind the "show the command" promise made in the README: the user must always
/// be able to see which command ran underneath and to repeat it in their terminal.
/// </remarks>
public interface IGitCommandLog
{
    void Record(GitResult result);

    void RecordFailure(GitCommand command, TimeSpan duration, string reason);

    /// <summary>
    /// Raised when a new entry is added (P06-T16).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>NOT on the UI thread</b>: git processes run on pool threads. The listening side has to
    /// marshal to its own context.
    /// </remarks>
    event EventHandler<GitCommandLogEntry>? Recorded;
}

/// <summary>
/// The record of a single command run.
/// </summary>
public sealed record GitCommandLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The command text that can be shown to the user.</summary>
    public required string CommandLine { get; init; }

    public required string WorkingDirectory { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary><see langword="null"/> when the process never completed (a timeout, a cancellation).</summary>
    public int? ExitCode { get; init; }

    public bool IsSuccess { get; init; }

    /// <summary>The stderr output, or the reason it did not complete.</summary>
    public string Details { get; init; } = string.Empty;
}

/// <summary>
/// A ring buffer keeping the last N commands in memory.
/// </summary>
/// <remarks>
/// Kept without a limit it turns into a memory leak over a long session; scrolling the graph can
/// produce thousands of commands.
/// </remarks>
public sealed class InMemoryGitCommandLog : IGitCommandLog
{
    private readonly ConcurrentQueue<GitCommandLogEntry> _entries = new();
    private readonly int _capacity;

    public InMemoryGitCommandLog(int capacity = 500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>Kaydedilen komutlar, en eskiden en yeniye.</summary>
    public IReadOnlyList<GitCommandLogEntry> Entries => [.. _entries];

    public void Record(GitResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Add(new GitCommandLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            CommandLine = result.Command.ToDisplayString(),
            WorkingDirectory = result.Command.WorkingDirectory,
            Duration = result.Duration,
            ExitCode = result.ExitCode,
            IsSuccess = result.IsSuccess,
            Details = result.StandardError,
        });
    }

    public void RecordFailure(GitCommand command, TimeSpan duration, string reason)
    {
        ArgumentNullException.ThrowIfNull(command);

        Add(new GitCommandLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            CommandLine = command.ToDisplayString(),
            WorkingDirectory = command.WorkingDirectory,
            Duration = duration,
            ExitCode = null,
            IsSuccess = false,
            Details = reason,
        });
    }

    public event EventHandler<GitCommandLogEntry>? Recorded;

    private void Add(GitCommandLogEntry entry)
    {
        _entries.Enqueue(entry);
        Recorded?.Invoke(this, entry);

        while (_entries.Count > _capacity && _entries.TryDequeue(out _))
        {
            // Kapasiteye inene kadar en eskileri at.
        }
    }
}

/// <summary>
/// A log that records nothing — used when logging is not wanted.
/// </summary>
public sealed class NullGitCommandLog : IGitCommandLog
{
    public static NullGitCommandLog Instance { get; } = new();

    private NullGitCommandLog()
    {
    }

    public void Record(GitResult result)
    {
    }

    public void RecordFailure(GitCommand command, TimeSpan duration, string reason)
    {
    }

    /// <remarks>Never fires; adds and removes are silently ignored.</remarks>
    public event EventHandler<GitCommandLogEntry>? Recorded
    {
        add { }
        remove { }
    }
}
