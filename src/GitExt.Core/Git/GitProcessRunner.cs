using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitExt.Core.Git;

/// <summary>
/// <see cref="IGitProcessRunner"/> uygulaması — uygulamadaki tek <c>Process.Start</c> çağrısı burada.
/// </summary>
public sealed class GitProcessRunner : IGitProcessRunner
{
    private readonly string _executablePath;
    private readonly IGitCommandLog _commandLog;
    private readonly ILogger<GitProcessRunner> _logger;

    public GitProcessRunner(
        GitExecutable executable,
        IGitCommandLog? commandLog = null,
        ILogger<GitProcessRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(executable);

        _executablePath = executable.Path;
        _commandLog = commandLog ?? NullGitCommandLog.Instance;
        _logger = logger ?? NullLogger<GitProcessRunner>.Instance;
    }

    public async Task<GitResult> RunAsync(
        GitCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Zaman aşımını iptal ile birleştir: hangisi önce olursa süreç öldürülür.
        using CancellationTokenSource timeoutSource = new(command.Timeout);
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            GitResult result = await ExecuteAsync(command, linkedSource.Token).ConfigureAwait(false);
            _commandLog.Record(result);

            if (!result.IsSuccess)
            {
                _logger.LogDebug(
                    "git çıkış kodu {ExitCode}: {Command}",
                    result.ExitCode,
                    command.ToDisplayString());
            }

            return result;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested
                                                 && !cancellationToken.IsCancellationRequested)
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
            _commandLog.RecordFailure(command, elapsed, $"zaman aşımı ({command.Timeout})");

            throw new GitException(
                GitFailureKind.Timeout,
                $"Komut {command.Timeout.TotalSeconds:F0} saniye içinde tamamlanmadı ve durduruldu.",
                command.ToDisplayString(),
                exitCode: -1,
                standardError: string.Empty);
        }
    }

    private async Task<GitResult> ExecuteAsync(GitCommand command, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _executablePath,
            WorkingDirectory = command.WorkingDirectory,
            // Kabuk yorumlaması YOK — kullanıcı verisi asla komut satırı olarak ayrıştırılmaz.
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

        long startedAt = Stopwatch.GetTimestamp();

        using Process process = new() { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new GitNotFoundException($"git süreci başlatılamadı: {_executablePath}");
        }

        try
        {
            // stdout ve stderr AYNI ANDA okunmalı. Biri dolup bloke olursa süreç yazamaz ve
            // asla bitmez — büyük çıktılarda klasik deadlock. Bu yüzden üç iş paralel yürür.
            Task<byte[]> stdoutTask = ReadAllBytesAsync(process, cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            Task stdinTask = WriteStandardInputAsync(process, command, cancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask, stdinTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new GitResult(
                command,
                process.ExitCode,
                await stdoutTask.ConfigureAwait(false),
                await stderrTask.ConfigureAwait(false),
                Stopwatch.GetElapsedTime(startedAt));
        }
        catch (OperationCanceledException)
        {
            // İptal edilen süreç GERÇEKTEN öldürülmeli; aksi halde arkada çalışmaya devam eder
            // ve index.lock gibi kaynakları tutar.
            TryKill(process);
            throw;
        }
    }

    /// <summary>
    /// stdout'u ham bayt olarak okur.
    /// </summary>
    /// <remarks>
    /// <see cref="Process.StandardOutput"/> yerine <see cref="StreamReader.BaseStream"/> kullanılır:
    /// dosya adları geçerli UTF-8 olmayabilir ve <c>git show</c> binary içerik döndürebilir.
    /// Metne çevirme kararı çağıranın olmalı.
    /// </remarks>
    private static async Task<byte[]> ReadAllBytesAsync(Process process, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await process.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
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
            // Süreç stdin'i okumadan çıkmış olabilir (kırık boru). Bu bir hata değil;
            // asıl sonuç çıkış kodundan anlaşılır.
        }
        finally
        {
            // stdin MUTLAKA kapatılmalı. Kapatılmazsa girdi bekleyen komutlar (`git commit -F -`
            // gibi) asla bitmez.
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
            // Süreç bu arada kendiliğinden bitmiş olabilir.
            _logger.LogTrace(ex, "git süreci öldürülemedi; muhtemelen zaten sonlanmıştı.");
        }
    }
}
