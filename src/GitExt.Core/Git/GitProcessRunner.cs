using System.Diagnostics;
using System.Text;
using GitExt.Core.Diagnostics;
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

        // Zaman aşımını iptal ile birleştir: hangisi önce olursa süreç öldürülür.
        using CancellationTokenSource timeoutSource = new(command.Timeout);
        using CancellationTokenSource linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        long startedAt = Stopwatch.GetTimestamp();

        // Süren komut teşhis panelinde görünsün: donmuş bir arayüzün sebebi çoğu zaman
        // burada saatlerdir duran tek bir çağrıdır (P09-T03).
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

        // Akış yolu da izleniyor: `log` gibi uzun sürenler tam olarak burada çalışıyor.
        using IDisposable tracked = _diagnostics.TrackOperation(command.ToDisplayString());

        using Process process = new() { StartInfo = BuildStartInfo(command) };

        if (!process.Start())
        {
            throw new GitNotFoundException($"The git process could not be started: {_executablePath}");
        }

        // stderr paralel olarak boşaltılmalı; aksi halde boru dolduğunda süreç bloke olur
        // ve akış asla ilerlemez.
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
    /// stdout'u okurken NUL sınırlarında parçalara ayırır.
    /// </summary>
    /// <remarks>
    /// UTF-8 çok baytlı karakterler okuma sınırına denk gelebileceği için, çözümleme
    /// bayt düzeyinde biriktirilip parça tamamlandığında yapılır.
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

            // Akışın sonunda kalan veri: git son kaydın ardına da NUL koyduğu için bu
            // normalde boştur. Boş değilse gerçek bir parçadır, atlanmamalı.
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

        // Komuta özel ortam EN SONA: kimlik doğrulama, GitEnvironment'ın boşalttığı
        // `GIT_ASKPASS`/`SSH_ASKPASS` değerlerini bilerek geri koyuyor (P06-T09).
        if (command.Environment is { Count: > 0 } overrides)
        {
            foreach ((string name, string value) in overrides)
            {
                startInfo.Environment[name] = value;
            }
        }

        // Flatpak sandbox'ındaysak git host üzerinde çalıştırılıyor (ADR-0009).
        // EN SONDA: ortam ve argümanların tamamı kurulduktan sonra sarmalanmalı,
        // aksi halde sonradan eklenenler host'a geçmez.
        SandboxLauncher.RewriteForHost(startInfo);

        return startInfo;
    }

    private async Task<GitResult> ExecuteAsync(GitCommand command, CancellationToken cancellationToken)
    {
        // Kabuk yorumlaması YOK — kullanıcı verisi asla komut satırı olarak ayrıştırılmaz.
        long startedAt = Stopwatch.GetTimestamp();

        using Process process = new() { StartInfo = BuildStartInfo(command) };

        if (!process.Start())
        {
            throw new GitNotFoundException($"The git process could not be started: {_executablePath}");
        }

        try
        {
            // stdout ve stderr AYNI ANDA okunmalı. Biri dolup bloke olursa süreç yazamaz ve
            // asla bitmez — büyük çıktılarda klasik deadlock. Bu yüzden üç iş paralel yürür.
            Task<(byte[] Bytes, bool Truncated)> stdoutTask =
                ReadAllBytesAsync(process, command.MaximumOutputBytes, cancellationToken);

            Task<string> stderrTask = ReadStandardErrorAsync(process, command.Progress, cancellationToken);
            Task stdinTask = WriteStandardInputAsync(process, command, cancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask, stdinTask).ConfigureAwait(false);

            (byte[] bytes, bool truncated) = await stdoutTask.ConfigureAwait(false);

            if (truncated)
            {
                // Çıktıyı okumayı bıraktık; süreç yazmaya çalışırken bloke kalır. Beklemek
                // yerine öldürülmeli, aksi halde `WaitForExitAsync` asla dönmez.
                TryKill(process);
            }

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
            // İptal edilen süreç GERÇEKTEN öldürülmeli; aksi halde arkada çalışmaya devam eder
            // ve index.lock gibi kaynakları tutar.
            TryKill(process);
            throw;
        }
    }

    /// <summary>
    /// stderr'i okur; ilerleme isteniyorsa <b>akış hâlinde</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — ilerleme satırları <c>\r</c> ile ayrılıyor</b> (gerçek klonda 404
    /// <c>\r</c>'ye karşılık 7 <c>\n</c>). Bu yüzden satır okuyucu değil <b>parça</b>
    /// okuyucu kullanılıyor: <c>ReadLineAsync</c> ilk <c>\n</c>'e kadar bekler, yani
    /// ilerleme ancak iş bittikten sonra görünürdü.
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
    /// stdout'u ham bayt olarak okur.
    /// </summary>
    /// <remarks>
    /// <see cref="Process.StandardOutput"/> yerine <see cref="StreamReader.BaseStream"/> kullanılır:
    /// dosya adları geçerli UTF-8 olmayabilir ve <c>git show</c> binary içerik döndürebilir.
    /// Metne çevirme kararı çağıranın olmalı.
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

                // Sınır aşıldı: okumayı bırak. Süreç çağıran tarafından sonlandırılacak;
                // okumaya devam etmek tam da kaçınmak istediğimiz belleği tüketmek olurdu.
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
            _logger.LogTrace(ex, "The git process could not be killed; it had probably already exited.");
        }
    }
}
