using System.Collections.Concurrent;

namespace GitExt.Core.Git;

/// <summary>
/// Çalıştırılan her <c>git</c> komutunun kaydı.
/// </summary>
/// <remarks>
/// README'de verilen "komutu göster" sözünün altyapısı: kullanıcı her zaman arkada hangi
/// komutun çalıştığını görebilmeli ve onu terminalinde tekrarlayabilmeli.
/// </remarks>
public interface IGitCommandLog
{
    void Record(GitResult result);

    void RecordFailure(GitCommand command, TimeSpan duration, string reason);
}

/// <summary>
/// Tek bir komut çalıştırma kaydı.
/// </summary>
public sealed record GitCommandLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Kullanıcıya gösterilebilecek komut metni.</summary>
    public required string CommandLine { get; init; }

    public required string WorkingDirectory { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary>Süreç hiç tamamlanmadıysa (zaman aşımı, iptal) <see langword="null"/>.</summary>
    public int? ExitCode { get; init; }

    public bool IsSuccess { get; init; }

    /// <summary>stderr çıktısı veya tamamlanmama nedeni.</summary>
    public string Details { get; init; } = string.Empty;
}

/// <summary>
/// Son N komutu bellekte tutan halka tampon (ring buffer).
/// </summary>
/// <remarks>
/// Sınır olmadan tutulursa uzun bir oturumda bellek sızıntısına dönüşür; grafiği kaydırmak
/// binlerce komut üretebilir.
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

    private void Add(GitCommandLogEntry entry)
    {
        _entries.Enqueue(entry);

        while (_entries.Count > _capacity && _entries.TryDequeue(out _))
        {
            // Kapasiteye inene kadar en eskileri at.
        }
    }
}

/// <summary>
/// Hiçbir şey kaydetmeyen günlük — günlükleme istenmediğinde kullanılır.
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
}
