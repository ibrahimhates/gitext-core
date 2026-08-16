using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Pull's merge strategy (P06-T07).</summary>
public enum PullStrategy
{
    /// <summary>
    /// Whatever the user's setting says.
    /// </summary>
    /// <remarks>
    /// This value is <b>never passed to the command</b>: it is resolved to the actual
    /// strategy via <see cref="IPullWriter.ResolveStrategyAsync"/> and an <b>explicit</b>
    /// flag is written to the command. The rationale is in the <see cref="PullWriter"/>
    /// notes.
    /// </remarks>
    Default,

    /// <summary><c>--no-rebase</c>: merge the remote branch into the current branch.</summary>
    Merge,

    /// <summary><c>--rebase</c>: move local commits on top of the remote branch.</summary>
    Rebase,

    /// <summary><c>--ff-only</c>: only if it can fast-forward.</summary>
    FastForwardOnly,
}

/// <summary><b>Where</b> the strategy came from — shown to the user (P06-T07).</summary>
public enum PullStrategySource
{
    /// <summary>The user chose it on this screen.</summary>
    UserChoice,

    /// <summary>The <c>branch.&lt;name&gt;.rebase</c> setting.</summary>
    BranchSetting,

    /// <summary>The <c>pull.rebase</c> setting.</summary>
    PullRebaseSetting,

    /// <summary>The <c>pull.ff</c> setting.</summary>
    PullFfSetting,

    /// <summary>No setting at all; the application's default (merge).</summary>
    ApplicationDefault,
}

/// <param name="Strategy">The strategy to apply.</param>
/// <param name="Source">The source of the decision.</param>
/// <param name="ConfigValue">The raw value of the setting, if any.</param>
public sealed record ResolvedPullStrategy(
    PullStrategy Strategy,
    PullStrategySource Source,
    string? ConfigValue);

/// <summary>Pull options (P06-T07).</summary>
public sealed record PullOptions
{
    /// <summary>Which remote? If <see langword="null"/>, the branch's upstream.</summary>
    public string? Remote { get; init; }

    /// <summary>Which remote branch? If <see langword="null"/>, the upstream's branch.</summary>
    public string? Branch { get; init; }

    /// <summary>The strategy; resolved from settings if <see cref="PullStrategy.Default"/>.</summary>
    public PullStrategy Strategy { get; init; }

    /// <summary>
    /// <c>--autostash</c>: stash work in a dirty tree and restore it afterward.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — restoring the stash CAN CONFLICT and the exit code is still 0.</b>
    /// In that case the working tree ends up with <c>UU</c> files and <b>conflict
    /// markers inside the file</b>, and the stash still sits in the list. The result is
    /// reported separately via <see cref="PullResult.AutoStashConflict"/>.
    /// </remarks>
    public bool AutoStash { get; init; }

    /// <summary><c>--prune</c>: prune during the fetch stage.</summary>
    public bool Prune { get; init; }

    /// <summary>Tag behavior (fetch stage).</summary>
    public FetchTagMode Tags { get; init; }

    /// <summary>
    /// The HTTPS credentials supplied by the user (P06-T09).
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, git uses its own channels (credential helper, SSH
    /// agent) — measured, both work fine in our environment. When set, the value is
    /// passed via <c>GIT_ASKPASS</c>; it is <b>not written</b> to the command line.
    /// </remarks>
    public GitCredentials? Credentials { get; init; }

    /// <summary>Live progress notification (P06-T10).</summary>
    public IProgress<GitProgress>? Progress { get; init; }
}

/// <summary>The result of a pull (P06-T07).</summary>
public sealed record PullResult
{
    /// <summary>The strategy that was actually applied, and its source.</summary>
    public required ResolvedPullStrategy Strategy { get; init; }

    /// <summary><c>HEAD</c> before the pull.</summary>
    public required string HeadBefore { get; init; }

    /// <summary><c>HEAD</c> after the pull.</summary>
    public required string HeadAfter { get; init; }

    /// <summary>Remote-tracking refs that changed during the fetch stage.</summary>
    public IReadOnlyList<RefChange> Changes { get; init; } = [];

    /// <summary>
    /// Are there any unresolved files left?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Cannot be determined</b> from the exit code alone: a conflict yields rc=1,
    /// but an <c>--autostash</c> restore conflict yields rc <b>0</b>. The state is read
    /// separately.
    /// </remarks>
    public bool HasConflicts { get; init; }

    /// <summary>Did the conflict come from restoring the stash?</summary>
    /// <remarks>
    /// This must be distinguished: what the user needs to do differs. The pull itself
    /// <b>succeeded</b>; what needs resolving is their own uncommitted change, and the
    /// stash is still there.
    /// </remarks>
    public bool AutoStashConflict { get; init; }

    /// <summary>Did <c>HEAD</c> not move at all ("already up to date")?</summary>
    public bool AlreadyUpToDate => string.Equals(HeadBefore, HeadAfter, StringComparison.Ordinal);

    /// <summary>
    /// The <b>runnable</b> command to revert the pull.
    /// </summary>
    /// <remarks>
    /// MEASURED: git sets <c>ORIG_HEAD</c> to the pre-pull commit in all three paths
    /// (fast-forward, merge, rebase), and <c>reset --hard</c> restores the previous
    /// state exactly. Even so, the <b>hash is written</b>, not <c>ORIG_HEAD</c>: the
    /// next merge/rebase overwrites it, and the user might run the command half an hour
    /// later.
    /// </remarks>
    public string RecoveryCommand => $"git reset --hard {HeadBefore}";
}

/// <summary>Pull operations (P06-T07).</summary>
public interface IPullWriter
{
    /// <summary>
    /// Reports <b>which strategy</b> will be applied based on the user's settings.
    /// </summary>
    /// <remarks>
    /// The UI shows this <b>before the pull</b>: the README's "show the command"
    /// principle and the plan's rule that "what the pull button does must not remain
    /// ambiguous".
    /// </remarks>
    Task<ResolvedPullStrategy> ResolveStrategyAsync(
        string workingDirectory,
        PullStrategy requested = PullStrategy.Default,
        CancellationToken cancellationToken = default);

    /// <summary>Performs a pull.</summary>
    Task<PullResult> PullAsync(
        string workingDirectory,
        PullOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A <c>git pull</c> wrapper (P06-T07).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The strategy is NEVER left up to git.</b> MEASURED: with no setting present and
/// diverged branches, <c>git pull</c> <b>refuses to run</b> (exit code 128) and prints a
/// nine-line <c>hint:</c> block. Worse: it <b>completes the fetch stage before
/// refusing</b>, meaning the repository has changed but the user sees "failed". That is
/// why the strategy is resolved first via <see cref="ResolveStrategyAsync"/>, and an
/// <b>always-explicit flag</b> (<c>--rebase</c>/<c>--no-rebase</c>/<c>--ff-only</c>) is
/// written to the command.
/// </para>
/// <para>
/// <b>Setting priority measured:</b> <c>branch.&lt;name&gt;.rebase</c> <b>overrides</b>
/// <c>pull.rebase</c> (<c>pull.rebase=true</c> + <c>branch.main.rebase=false</c> →
/// a merge was performed).
/// </para>
/// </remarks>
public sealed class PullWriter : IPullWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly IGitConfigReader _config;

    public PullWriter(IGitWriter writer, IGitProcessRunner runner, IGitConfigReader config)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(config);

        _writer = writer;
        _runner = runner;
        _config = config;
    }

    public async Task<ResolvedPullStrategy> ResolveStrategyAsync(
        string workingDirectory,
        PullStrategy requested = PullStrategy.Default,
        CancellationToken cancellationToken = default)
    {
        if (requested != PullStrategy.Default)
        {
            return new ResolvedPullStrategy(requested, PullStrategySource.UserChoice, null);
        }

        string? branch = await CurrentBranchAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        // Sıra ölçümle sabit: dal ayarı en güçlüsü.
        if (branch is { Length: > 0 })
        {
            string? branchSetting = await _config
                .GetAsync(workingDirectory, $"branch.{branch}.rebase", cancellationToken)
                .ConfigureAwait(false);

            if (ParseRebase(branchSetting) is { } fromBranch)
            {
                return new ResolvedPullStrategy(
                    fromBranch, PullStrategySource.BranchSetting, branchSetting);
            }
        }

        string? pullRebase = await _config
            .GetAsync(workingDirectory, "pull.rebase", cancellationToken)
            .ConfigureAwait(false);

        if (ParseRebase(pullRebase) is { } fromPull)
        {
            return new ResolvedPullStrategy(
                fromPull, PullStrategySource.PullRebaseSetting, pullRebase);
        }

        string? pullFf = await _config
            .GetAsync(workingDirectory, "pull.ff", cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(pullFf, "only", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedPullStrategy(
                PullStrategy.FastForwardOnly, PullStrategySource.PullFfSetting, pullFf);
        }

        // git'in bu noktadaki davranışı "reddet"; bizimki git'in belgelediği tarihsel
        // varsayılan olan birleştirme. Kullanıcı ekranda ne olacağını görüyor ve
        // değiştirebiliyor, yani sessiz bir tercih değil.
        return new ResolvedPullStrategy(
            PullStrategy.Merge, PullStrategySource.ApplicationDefault, null);
    }

    public async Task<PullResult> PullAsync(
        string workingDirectory,
        PullOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        ResolvedPullStrategy strategy = await ResolveStrategyAsync(
                workingDirectory, options.Strategy, cancellationToken)
            .ConfigureAwait(false);

        string before = await RevisionAsync(workingDirectory, "HEAD", cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> refsBefore =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken)
                .ConfigureAwait(false);

        string standardError;

        try
        {
            using AskPassSession? askPass = options.Credentials is { } credentials
                ? AskPassSession.Create(credentials)
                : null;

            GitResult result = await _writer
                .RunWithEnvironmentAsync(
                    workingDirectory,
                    BuildArguments(options, strategy.Strategy),
                    askPass?.Environment,
                    options.Progress,
                    cancellationToken)
                .ConfigureAwait(false);

            standardError = result.StandardError;
        }
        catch (GitException error)
        {
            // Çakışma bir "hata" değil, bir sonuç: kullanıcı dosyaları çözecek. İstisna
            // olarak yükselirse arayüz ne olduğunu anlatamaz, yalnızca kırmızı bir kutu
            // gösterir — oysa depo şu an çakışma durumunda ve yapılacak iş belli.
            //
            // 🔴 Ayrım `Kind`'a göre YAPILAMIYOR: ÖLÇÜLDÜ, pull'un çakışma metni
            // (`Auto-merging…`, `CONFLICT (content):`, `Automatic merge failed`) **stdout'a**
            // yazılıyor; sınıflandırıcı yalnızca stderr'i gördüğü için `Unknown` diyor.
            // Bu yüzden karar metne değil **duruma** bakılarak veriliyor: birleşmemiş dosya
            // var mı? Kanal değişse bile bu doğru kalır.
            if (!await HasUnmergedAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            standardError = error.StandardError;
        }

        string after = await RevisionAsync(workingDirectory, "HEAD", cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<string, string> refsAfter =
            await RefSnapshot.ReadAsync(_runner, workingDirectory, cancellationToken)
                .ConfigureAwait(false);

        bool conflicts = await HasUnmergedAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return new PullResult
        {
            Strategy = strategy,
            HeadBefore = before,
            HeadAfter = after,
            Changes = RefSnapshot.Diff(refsBefore, refsAfter),
            HasConflicts = conflicts,

            // 🔴 Ayrım git'in kendi metninden: geri koyma çakışmasında çıkış kodu 0 ve
            // özel bir açıklama yazılıyor. Bunu ayırmazsak kullanıcıya "birleştirme
            // çakıştı" derdik — oysa birleştirme başarılı, çakışan onun kendi
            // kaydedilmemiş değişikliği.
            AutoStashConflict = conflicts
                && standardError.Contains("applying them", StringComparison.Ordinal)
                && standardError.Contains("stash", StringComparison.OrdinalIgnoreCase),
        };
    }

    private static IReadOnlyList<string> BuildArguments(PullOptions options, PullStrategy strategy)
    {
        List<string> arguments = ["pull", "--progress"];

        arguments.Add(strategy switch
        {
            PullStrategy.Rebase => "--rebase",
            PullStrategy.FastForwardOnly => "--ff-only",

            // ⚠️ `--no-rebase` AÇIKÇA yazılıyor. Bayraksız çağrı, ayarsız ve iraksayan
            // depoda git'i "reddet" moduna sokuyor (ölçüldü, rc=128).
            _ => "--no-rebase",
        });

        if (options.AutoStash)
        {
            arguments.Add("--autostash");
        }

        if (options.Prune)
        {
            arguments.Add("--prune");
        }

        switch (options.Tags)
        {
            case FetchTagMode.All:
                arguments.Add("--tags");
                break;
            case FetchTagMode.None:
                arguments.Add("--no-tags");
                break;
            case FetchTagMode.Default:
            default:
                break;
        }

        if (options.Remote is { Length: > 0 } remote)
        {
            arguments.Add("--");
            arguments.Add(remote);

            if (options.Branch is { Length: > 0 } branch)
            {
                arguments.Add(branch);
            }
        }

        return arguments;
    }

    /// <summary>
    /// <c>branch.&lt;dal&gt;.rebase</c> / <c>pull.rebase</c> değerini stratejiye çevirir.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: git <c>true</c>, <c>false</c>, <c>only</c>, <c>interactive</c>,
    /// <c>merges</c> değerlerini kabul ediyor. <c>interactive</c> ve <c>merges</c> bizde
    /// düz rebase'e düşüyor — etkileşimli rebase P07-T10'un konusu ve <b>burada</b>
    /// açılacak bir editör kullanıcıyı şaşırtırdı.
    /// </remarks>
    private static PullStrategy? ParseRebase(string? value) => value?.ToLowerInvariant() switch
    {
        "true" or "yes" or "on" or "1" or "interactive" or "merges" => PullStrategy.Rebase,
        "false" or "no" or "off" or "0" => PullStrategy.Merge,
        _ => null,
    };

    private async Task<string?> CurrentBranchAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["symbolic-ref", "-q", "--short", "HEAD"],

                // Ayrık HEAD'de çıkış kodu 1; bu bir hata değil, "dal yok" cevabı (P02-T09).
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0
            ? result.GetStandardOutputText().Trim()
            : null;
    }

    private async Task<string> RevisionAsync(
        string workingDirectory,
        string revision,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", revision],

                // Doğmamış HEAD: çıkış kodu 1, çıktı boş.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.GetStandardOutputText().Trim();
    }

    private async Task<bool> HasUnmergedAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        return result.StandardOutput.Length > 0;
    }

}
