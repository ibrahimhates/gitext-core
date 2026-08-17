using System.Diagnostics;
using System.Text;
using GitExt.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitExt.Core.Git;

/// <summary>
/// <see cref="IGitProcessRunner"/> implementation — the application's only <c>Process.Start</c>
/// call lives here.
/// </summary>
public sealed class GitProcessRunner : IGitProcessRunner
{
    private readonly string _executablePath;
    private readonly IGitCommandLog _commandLog;
    private readonly IPerformanceDiagnostics _diagnostics;
    private readonly ILogger<GitProcessRunner> _logger;

    public GitProcessRunner(
        GitExecutable executable,
        IGitCommandLog? commandLog = null,
        ILogger<GitProcessRunner>? logger = null,
        IPerformanceDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(executable);

        _executablePath = executable.Path;
        _commandLog = commandLog ?? NullGitCommandLog.Instance;
        _diagnostics = diagnostics ?? NullPerformanceDiagnostics.Instance;
        _logger = logger ?? NullLogger<GitProcessRunner>.Instance;
    }

    public async Task<GitResult> RunAsync(
        GitCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Combine the timeout with cancellation: whichever comes first kills the process.
        using CancellationTokenSource timeoutSource = new(command.Timeout);
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        long startedAt = Stopwatch.GetTimestamp();

        // Make the running command visible in the diagnostics panel: the cause of a frozen UI is
        // most often a single call that has been sitting here for hours (P09-T03).
        using IDisposable tracked = _diagnostics.TrackOperation(command.ToDisplayString());

        try
        {
            GitResult result = await ExecuteAsync(command, linkedSource.Token).ConfigureAwait(false);
            _commandLog.Record(result);

            if (!result.IsSuccess)
            {
                _logger.LogDebug(
                    "git exit code {ExitCode}: {Command}",
                    result.ExitCode,
                    command.ToDisplayString());
            }

            return result;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            _commandLog.RecordFailure(command, elapsed, $"timeout ({command.Timeout})");

            throw new GitException(
                GitFailureKind.Timeout,
                $"The command did not finish within {command.Timeout.TotalSeconds:F0} seconds and was stopped.",
                command.ToDisplayString(),
                exitCode: -1,
                standardError: string.Empty);
        }
    }

    public async IAsyncEnumerable<string> StreamNulSeparatedAsync(
        GitCommand command,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using CancellationTokenSource timeoutSource = new(command.Timeout);
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        CancellationToken token = linkedSource.Token;
        long startedAt = Stopwatch.GetTimestamp();

        // The streaming path is tracked as well: long-running ones such as `log` run exactly here.
        using IDisposable tracked = _diagnostics.TrackOperation(command.ToDisplayString());

        using Process process = new() { StartInfo = BuildStartInfo(command) };

        if (!process.Start())
        {
            throw new GitNotFoundException($"The git process could not be started: {_executablePath}");
        }

        // stderr must be drained in parallel; otherwise the process blocks when the pipe fills up
        // and the stream never makes progress.
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(token);
        Task stdinTask = WriteStandardInputAsync(process, command, token);

        try
        {
            await foreach (string readToken in ReadNulSeparatedAsync(process, token).ConfigureAwait(false))
            {
                yield return readToken;
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                TryKill(process);
            }
        }

        await stdinTask.ConfigureAwait(false);
        string standardError = await stderrTask.ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false);

        GitResult result = new(
            command,
            process.ExitCode,
            [],
            standardError,
            Stopwatch.GetElapsedTime(startedAt));

        _commandLog.Record(result);

        if (!result.IsSuccess)
        {
            GitFailureKind kind = GitFailureClassifier.Classify(standardError);

            throw new GitException(
                kind,
                GitFailureClassifier.Describe(kind),
                command.ToDisplayString(),
                process.ExitCode,
                standardError);
        }
    }

    /// <summary>
    /// Splits stdout into pieces at NUL boundaries while reading it.
    /// </summary>
    /// <remarks>
    /// Because UTF-8 multi-byte characters can straddle a read boundary, the data is accumulated
    /// at the byte level and decoded once a piece is complete.
    /// </remarks>
    private static async IAsyncEnumerable<string> ReadNulSeparatedAsync(
        Process process,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        Stream stdout = process.StandardOutput.BaseStream;

        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(16 * 1024);
        using MemoryStream pending = new();

        try
        {
            int read;
            while ((read = await stdout.ReadAsync(buffer.AsMemory(), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                int start = 0;

                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != 0)
                    {
                        continue;
                    }

                    pending.Write(buffer, start, i - start);
                    yield return DecodeAndReset(pending);
                    start = i + 1;
                }

                pending.Write(buffer, start, read - start);
            }

            // Data left at the end of the stream: because git puts a NUL after the last record as
            // well, this is normally empty. If it is not empty it is a real piece and must not be
            // skipped.
            if (pending.Length > 0)
            {
                yield return DecodeAndReset(pending);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string DecodeAndReset(MemoryStream pending)
    {
        string value = _utf8Lenient.GetString(pending.GetBuffer(), 0, (int)pending.Length);
        pending.SetLength(0);
        return value;
    }

    private static readonly System.Text.UTF8Encoding _utf8Lenient =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    private ProcessStartInfo BuildStartInfo(GitCommand command)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _executablePath,
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        foreach (string argument in GitEnvironment.ConfigurationOverrides())
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        GitEnvironment.Apply(startInfo, command.IsReadOnly);

        // Command-specific environment goes LAST: authentication deliberately puts back the
        // `GIT_ASKPASS`/`SSH_ASKPASS` values that GitEnvironment cleared (P06-T09).
        if (command.Environment is { Count: > 0 } overrides)
        {
            foreach ((string name, string value) in overrides)
            {
                startInfo.Environment[name] = value;
            }
        }

        // If we are inside a Flatpak sandbox, git is run on the host (ADR-0009).
        // LAST: the wrapping must happen after the environment and all arguments are set up,
        // otherwise anything added afterwards does not make it to the host.
        SandboxLauncher.RewriteForHost(startInfo);

        return startInfo;
    }

    private async Task<GitResult> ExecuteAsync(GitCommand command, CancellationToken cancellationToken)
    {
        // NO shell interpretation — user data is never parsed as a command line.
        long startedAt = Stopwatch.GetTimestamp();

        using Process process = new() { StartInfo = BuildStartInfo(command) };

        if (!process.Start())
        {
            throw new GitNotFoundException($"The git process could not be started: {_executablePath}");
        }

        try
        {
            // stdout and stderr must be read AT THE SAME TIME. If one fills up and blocks, the
            // process cannot write and never finishes — the classic deadlock on large outputs.
            // That is why the three tasks run in parallel.
            Task<(byte[] Bytes, bool Truncated)> stdoutTask =
                ReadAllBytesAsync(process, command.MaximumOutputBytes, cancellationToken);

            Task<string> stderrTask = ReadStandardErrorAsync(process, command.Progress, cancellationToken);
            Task stdinTask = WriteStandardInputAsync(process, command, cancellationToken);

            // 🔴 MEASURED — stdout is awaited ON ITS OWN, before the other two. Waiting for all
            // three together (`Task.WhenAll`) DEADLOCKS as soon as the output limit trips:
            // reading stops, so git blocks writing into a full stdout pipe; blocked, it never
            // exits; not exiting, it never closes stderr — and the wait for stderr therefore never
            // ends. The command only came back at the 120-second timeout.
            // The pipe's buffer is what hides this: on Linux 64 KB is enough for the rest of a
            // small output, so git finishes anyway, while the Windows buffer (4 KB) blocks
            // immediately. The bug is not Windows-specific — with an output large enough it
            // reproduces on Linux too, and the test now uses such a size.
            (byte[] bytes, bool truncated) = await stdoutTask.ConfigureAwait(false);

            if (truncated)
            {
                // We stopped reading the output; the process stays blocked trying to write. It
                // has to be killed instead of waited for, otherwise `WaitForExitAsync` never
                // returns. This must happen BEFORE stderr is awaited — killing it is what lets
                // that wait finish.
                TryKill(process);
            }

            await Task.WhenAll(stderrTask, stdinTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new GitResult(
                command,
                process.ExitCode,
                bytes,
                await stderrTask.ConfigureAwait(false),
                Stopwatch.GetElapsedTime(startedAt),
                truncated);
        }
        catch (OperationCanceledException)
        {
            // A cancelled process must ACTUALLY be killed; otherwise it keeps running in the
            // background and holds resources such as index.lock.
            TryKill(process);
            throw;
        }
    }

    /// <summary>
    /// Reads stderr; <b>as a stream</b> if progress is requested.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — progress lines are separated by <c>\r</c></b> (in a real clone, 404
    /// <c>\r</c> against 7 <c>\n</c>). That is why a <b>chunk</b> reader is used rather than a
    /// line reader: <c>ReadLineAsync</c> waits for the first <c>\n</c>, meaning progress would
    /// only become visible after the work had finished.
    /// </remarks>
    private static async Task<string> ReadStandardErrorAsync(
        Process process,
        IProgress<GitProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        StringBuilder all = new();
        string remainder = string.Empty;
        char[] chunk = System.Buffers.ArrayPool<char>.Shared.Rent(8 * 1024);

        try
        {
            int read;

            while ((read = await process.StandardError
                       .ReadAsync(chunk.AsMemory(), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                string text = new(chunk, 0, read);
                all.Append(text);

                (IReadOnlyList<string> lines, remainder) =
                    GitProgressParser.SplitLines(remainder + text);

                foreach (string line in lines)
                {
                    if (GitProgressParser.Parse(line) is { } step)
                    {
                        progress.Report(step);
                    }
                }
            }

            if (remainder.Length > 0 && GitProgressParser.Parse(remainder) is { } last)
            {
                progress.Report(last);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(chunk);
        }

        return all.ToString();
    }

    /// <summary>
    /// Reads stdout as raw bytes.
    /// </summary>
    /// <remarks>
    /// <see cref="StreamReader.BaseStream"/> is used instead of <see cref="Process.StandardOutput"/>:
    /// file names may not be valid UTF-8 and <c>git show</c> can return binary content.
    /// The decision to convert to text must belong to the caller.
    /// </remarks>
    private static async Task<(byte[] Bytes, bool Truncated)> ReadAllBytesAsync(
        Process process,
        long? maximumBytes,
        CancellationToken cancellationToken)
    {
        Stream stream = process.StandardOutput.BaseStream;

        if (maximumBytes is not { } limit)
        {
            using MemoryStream all = new();
            await stream.CopyToAsync(all, cancellationToken).ConfigureAwait(false);
            return (all.ToArray(), false);
        }

        using MemoryStream buffer = new();
        byte[] chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);

        try
        {
            int read;

            while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);

                if (buffer.Length <= limit)
                {
                    continue;
                }

                // The limit was exceeded: stop reading. The process will be terminated by the
                // caller; continuing to read would consume exactly the memory we want to avoid.
                return (buffer.ToArray(), true);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(chunk);
        }

        return (buffer.ToArray(), false);
    }

    private static async Task WriteStandardInputAsync(
        Process process,
        GitCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            if (command.StandardInput is { } input)
            {
                await process.StandardInput.BaseStream
                    .WriteAsync(input, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // The process may have exited without reading stdin (broken pipe). This is not an
            // error; the real outcome is understood from the exit code.
        }
        finally
        {
            // stdin MUST be closed. If it is not, commands waiting for input (such as
            // `git commit -F -`) never finish.
            process.StandardInput.Close();
        }
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // The process may have exited on its own in the meantime.
            _logger.LogTrace(ex, "The git process could not be killed; it had probably already exited.");
        }
    }
}
