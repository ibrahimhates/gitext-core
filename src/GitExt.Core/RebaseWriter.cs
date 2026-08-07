using System.Globalization;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>Rebase seçenekleri (P07-T09, P07-T10).</summary>
public sealed record RebaseOptions
{
    /// <summary>Üzerine yeniden oynatılacak dal ya da commit.</summary>
    public required string Upstream { get; init; }

    /// <summary>
    /// <c>--onto</c>: commit'lerin taşınacağı yeni taban.
    /// </summary>
    /// <remarks>
    /// <c>upstream</c> "hangi commit'ler taşınacak"ı, <c>--onto</c> "nereye"yi belirliyor.
    /// İkisi ayrı olduğunda dalın tabanı değiştirilmiş oluyor.
    /// </remarks>
    public string? Onto { get; init; }

    /// <summary>Rebase edilecek dal; <see langword="null"/> ise mevcut dal.</summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Interactive rebase adımları; <see langword="null"/> ise düz rebase.
    /// </summary>
    public IReadOnlyList<RebaseStep>? Steps { get; init; }

    /// <summary><c>reword</c> için kullanıcının yazdığı mesaj.</summary>
    public string? NewMessage { get; init; }

    /// <summary><c>--autostash</c>: kirli ağacı geçici olarak kenara koy.</summary>
    /// <remarks>
    /// Rebase kirli bir ağaçta çalışmıyor. Autostash, kullanıcıyı "önce stash'le" diye
    /// geri göndermek yerine bunu kendisi yapıp sonunda geri koyuyor.
    /// </remarks>
    public bool AutoStash { get; init; }

    public bool IsInteractive => Steps is { Count: > 0 };
}

/// <summary>Rebase'in nasıl sonuçlandığı (P07-T09).</summary>
public enum RebaseOutcome
{
    /// <summary>Yapacak bir şey yoktu.</summary>
    AlreadyUpToDate,

    /// <summary>Tamamlandı.</summary>
    Completed,

    /// <summary>Çakışmayla durdu.</summary>
    Conflicted,

    /// <summary><c>edit</c> adımında kullanıcı için durdu.</summary>
    StoppedForEdit,
}

/// <summary>Rebase sonucu (P07-T09, P07-T10).</summary>
public sealed record RebaseResult
{
    public required RebaseOutcome Outcome { get; init; }

    public required SafetyPoint SafetyPoint { get; init; }

    public IReadOnlyList<RepositoryPath> ConflictedPaths { get; init; } = [];

    /// <summary>Kaçıncı adımdayız (<c>.git/rebase-merge/msgnum</c>).</summary>
    public int CurrentStep { get; init; }

    /// <summary>Toplam adım (<c>.git/rebase-merge/end</c>).</summary>
    public int TotalSteps { get; init; }

    public bool IsStopped => Outcome is RebaseOutcome.Conflicted or RebaseOutcome.StoppedForEdit;
}

/// <summary>Rebase işlemleri (P07-T09, P07-T10).</summary>
public interface IRebaseWriter
{
    Task<RebaseResult> RebaseAsync(
        string workingDirectory,
        RebaseOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Interactive rebase ekranını doldurmak için taşınacak commit'leri okur.
    /// </summary>
    Task<IReadOnlyList<RebaseStep>> ReadStepsAsync(
        string workingDirectory,
        string upstream,
        string? branch = null,
        CancellationToken cancellationToken = default);

    /// <summary>Süren rebase'i atlar (<c>--skip</c>).</summary>
    Task SkipAsync(string workingDirectory, CancellationToken cancellationToken = default);

    string DescribeCommand(RebaseOptions options);
}

/// <summary>
/// <c>git rebase</c> sarmalayıcısı (P07-T09, P07-T10).
/// </summary>
/// <remarks>
/// <para>
/// Interactive rebase, <c>GIT_SEQUENCE_EDITOR</c> üzerinden todo listesi yazılarak
/// yapılıyor — bkz. <see cref="RebaseTodoSession"/>. Plan bu mekanizmayı "faz başında
/// prototiple" diye işaretlemişti; ölçüldü ve çalışıyor.
/// </para>
/// <para>
/// ÖLÇÜLDÜ — çakışmada bırakılan durum <c>.git/rebase-merge/</c>: <c>head-name</c>
/// (özgün dal), <c>onto</c>, <c>msgnum</c>/<c>end</c> (ilerleme), <c>orig-head</c>.
/// <c>edit</c> adımında ayrıca <c>amend</c> dosyası oluşuyor ve <c>HEAD</c> <b>ayrık</b>
/// kalıyor — bu yüzden "hangi daldayız" sorusuna <c>head-name</c> cevap veriyor.
/// </para>
/// </remarks>
public sealed class RebaseWriter : IRebaseWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly ISafetyPointRecorder _safety;

    public RebaseWriter(IGitWriter writer, IGitProcessRunner runner, ISafetyPointRecorder safety)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(safety);

        _writer = writer;
        _runner = runner;
        _safety = safety;
    }

    public async Task<RebaseResult> RebaseAsync(
        string workingDirectory,
        RebaseOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Upstream);

        if (options.Steps is { } steps && RebaseTodo.Validate(steps) is { } problem)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        SafetyPoint point = await _safety
            .CaptureAsync(workingDirectory, "rebase", cancellationToken)
            .ConfigureAwait(false);

        using RebaseTodoSession? session = options.IsInteractive
            ? RebaseTodoSession.Create(RebaseTodo.Render(options.Steps!), options.NewMessage)
            : null;

        try
        {
            await _writer.RunWithEnvironmentAsync(
                workingDirectory,
                BuildArguments(options),
                session?.Environment ?? NonInteractiveEditor,
                progress: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (GitException)
        {
            // Çakışma ve `edit` duruşu birer DURUM; gerçek hatalar (kirli ağaç, bilinmeyen
            // upstream) olduğu gibi yukarı gitmeli. Ayrım rebase dizininin varlığından.
            RebaseState? state =
                await ReadStateAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

            if (state is null)
            {
                throw;
            }

            IReadOnlyList<RepositoryPath> conflicts =
                await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

            return new RebaseResult
            {
                Outcome = conflicts.Count > 0
                    ? RebaseOutcome.Conflicted
                    : RebaseOutcome.StoppedForEdit,
                SafetyPoint = point,
                ConflictedPaths = conflicts,
                CurrentStep = state.Current,
                TotalSteps = state.Total,
            };
        }

        // Çıkış kodu 0 olsa bile `edit` adımında durmuş olabiliriz: git bu durumda
        // "Stopped at …" yazıp BAŞARIYLA çıkıyor. Karar yine duruma bakarak veriliyor.
        RebaseState? after = await ReadStateAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (after is not null)
        {
            IReadOnlyList<RepositoryPath> conflicts =
                await ReadConflictsAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

            return new RebaseResult
            {
                Outcome = conflicts.Count > 0
                    ? RebaseOutcome.Conflicted
                    : RebaseOutcome.StoppedForEdit,
                SafetyPoint = point,
                ConflictedPaths = conflicts,
                CurrentStep = after.Current,
                TotalSteps = after.Total,
            };
        }

        string head = await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        return new RebaseResult
        {
            Outcome = string.Equals(head, point.ObjectId, StringComparison.Ordinal)
                ? RebaseOutcome.AlreadyUpToDate
                : RebaseOutcome.Completed,
            SafetyPoint = point,
        };
    }

    public async Task<IReadOnlyList<RebaseStep>> ReadStepsAsync(
        string workingDirectory,
        string upstream,
        string? branch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upstream);

        string range = $"{upstream}..{branch ?? "HEAD"}";

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory, "log", "--reverse", "--format=%x1e%H%x00%s", range),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return [];
        }

        List<RebaseStep> steps = [];

        foreach (string record in result.GetStandardOutputText()
                     .Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Trim('\n', '\r').Split('\0');

            if (fields.Length >= 2 && fields[0].Length > 0)
            {
                steps.Add(new RebaseStep { ObjectId = fields[0], Subject = fields[1] });
            }
        }

        return steps;
    }

    public Task SkipAsync(string workingDirectory, CancellationToken cancellationToken = default) =>
        _writer.RunWithEnvironmentAsync(
            workingDirectory,
            ["rebase", "--skip"],
            NonInteractiveEditor,
            progress: null,
            cancellationToken);

    public string DescribeCommand(RebaseOptions options) => Describe(options);

    /// <summary>Çalıştırılacak komutu üretir ("komutu göster" ilkesi).</summary>
    public static string Describe(RebaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return "git " + string.Join(' ', BuildArguments(options));
    }

    private static IReadOnlyList<string> BuildArguments(RebaseOptions options)
    {
        List<string> arguments = ["rebase"];

        if (options.IsInteractive)
        {
            arguments.Add("--interactive");
        }

        if (options.AutoStash)
        {
            arguments.Add("--autostash");
        }

        if (options.Onto is { Length: > 0 } onto)
        {
            arguments.Add("--onto");
            arguments.Add(onto);
        }

        arguments.Add(options.Upstream);

        if (options.Branch is { Length: > 0 } branch)
        {
            arguments.Add(branch);
        }

        return arguments;
    }

    /// <summary>Editörün arayüzü kilitlemesini imkânsız kılan ortam.</summary>
    private static IReadOnlyDictionary<string, string> NonInteractiveEditor =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_EDITOR"] = OperatingSystem.IsWindows() ? "cmd /c exit 0" : "true",
        };

    private sealed record RebaseState(int Current, int Total, string BranchName);

    /// <summary>
    /// Süren rebase'in durumunu <c>.git</c> altındaki dosyalardan okur.
    /// </summary>
    /// <remarks>
    /// ⚠️ Yol <c>--absolute-git-dir</c> ile alınıyor: <c>--git-path</c> göreli dönüyor ve
    /// çalışma dizinine bağlı (P05-T13'ün dersi).
    /// </remarks>
    private async Task<RebaseState?> ReadStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "--absolute-git-dir"),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return null;
        }

        string gitDirectory = result.GetStandardOutputText().Trim();

        foreach (string name in new[] { "rebase-merge", "rebase-apply" })
        {
            string directory = Path.Combine(gitDirectory, name);

            if (!Directory.Exists(directory))
            {
                continue;
            }

            return new RebaseState(
                ReadNumber(directory, "msgnum"),
                ReadNumber(directory, "end"),
                ReadLine(directory, "head-name"));
        }

        return null;
    }

    private static int ReadNumber(string directory, string fileName) =>
        int.TryParse(ReadLine(directory, fileName), CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;

    private static string ReadLine(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);

        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (IOException)
        {
            // Rebase tam o anda ilerliyor olabilir; okunamaması bir hata değil.
            return string.Empty;
        }
    }

    private async Task<IReadOnlyList<RepositoryPath>> ReadConflictsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return [];
        }

        List<RepositoryPath> paths = [];

        foreach (string value in result.GetStandardOutputText()
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (RepositoryPath.TryParse(value, out RepositoryPath path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private async Task<string> ReadHeadAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "HEAD"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? result.GetStandardOutputText().Trim() : string.Empty;
    }
}
