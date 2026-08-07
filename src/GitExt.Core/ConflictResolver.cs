using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>Çakışmada hangi tarafın alınacağı (P07-T05).</summary>
public enum ResolutionSide
{
    /// <summary>Bizim sürümümüz — <c>HEAD</c>.</summary>
    Ours,

    /// <summary>Karşı tarafın sürümü.</summary>
    Theirs,
}

/// <summary>
/// Çakışma çözümünün o anki durumu (P07-T05).
/// </summary>
public sealed record ConflictProgress
{
    /// <summary>Hangi işlemin ortasındayız?</summary>
    public required InProgressOperation Operation { get; init; }

    /// <summary>Hâlâ çözülmemiş dosyalar.</summary>
    public IReadOnlyList<RepositoryPath> Remaining { get; init; } = [];

    public int RemainingCount => Remaining.Count;

    /// <summary>Hepsi çözüldü mü?</summary>
    public bool IsResolved => Remaining.Count == 0;

    /// <summary>
    /// <c>--continue</c> sunulabilir mi?
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — çözülmeden <c>--continue</c> çalıştırmak rc=128 veriyor</b>
    /// (<c>Committing is not possible because you have unmerged files</c>). Düğmeyi erken
    /// etkinleştirmek kullanıcıyı anlamsız bir hataya sokardı.
    /// </remarks>
    public bool CanContinue => IsResolved && Operation != InProgressOperation.None;

    /// <summary>İşlemi sürdüren komut — ekranda gösteriliyor.</summary>
    public string? ContinueCommand => ConflictResolver.ContinueVerb(Operation) is { } verb
        ? $"git {verb} --continue"
        : null;

    /// <summary>İşlemi iptal eden komut.</summary>
    public string? AbortCommand => ConflictResolver.ContinueVerb(Operation) is { } verb
        ? $"git {verb} --abort"
        : null;
}

/// <summary>Çakışma çözüm akışı (P07-T05).</summary>
public interface IConflictResolver
{
    /// <summary>Kalan çakışmaları ve sunulabilecek eylemleri okur.</summary>
    Task<ConflictProgress> GetProgressAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Dosyayı çözülmüş olarak işaretler (<c>git add</c>).</summary>
    Task MarkResolvedAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken = default);

    /// <summary>Çakışmayı dosyayı <b>silerek</b> çözer (<c>git rm</c>).</summary>
    Task RemoveAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken = default);

    /// <summary>Bir tarafı bütünüyle alır ve çözülmüş işaretler.</summary>
    Task TakeSideAsync(
        string workingDirectory,
        RepositoryPath path,
        ResolutionSide side,
        CancellationToken cancellationToken = default);

    /// <summary>Elle düzenlenmiş içeriği yazar ve çözülmüş işaretler.</summary>
    Task WriteResolvedAsync(
        string workingDirectory,
        RepositoryPath path,
        byte[] content,
        CancellationToken cancellationToken = default);

    /// <summary>İşlemi sürdürür (<c>--continue</c>).</summary>
    Task ContinueAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>İşlemi iptal eder (<c>--abort</c>).</summary>
    Task AbortAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Çakışmaları çözüp süren işlemi sürdürür (P07-T05).
/// </summary>
/// <remarks>
/// <para>
/// <b>Devam/iptal komutu işleme göre değişiyor</b> — merge, rebase, cherry-pick, revert ve
/// <c>am</c> için ayrı fiiller. Hangi işlemin sürdüğü <see cref="IInProgressOperationReader"/>
/// ile <b>durum dosyalarından</b> okunuyor; git'in metnine bakılmıyor.
/// </para>
/// <para>
/// ℹ️ <b>ÖLÇÜLDÜ — <c>--continue</c> editör açmıyor.</b> Etkileşimli bir <c>core.editor</c>
/// ayarlıyken bile (60 sn uyuyan bir betikle denendi) merge/rebase/cherry-pick/revert
/// <c>--continue</c> editörü <b>hiç çağırmadı</b>: hazırlanmış mesajı yeniden kullanıyorlar.
/// Yine de <c>GIT_EDITOR</c> sabitleniyor — arayüzün bir editör beklerken donması, önlenmesi
/// ucuz ama yaşanırsa teşhisi pahalı bir hata.
/// </para>
/// </remarks>
public sealed class ConflictResolver : IConflictResolver
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly IInProgressOperationReader _operations;

    public ConflictResolver(
        IGitWriter writer,
        IGitProcessRunner runner,
        IInProgressOperationReader operations)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(operations);

        _writer = writer;
        _runner = runner;
        _operations = operations;
    }

    /// <summary>Süren işlemin <c>--continue</c>/<c>--abort</c> aldığı git alt komutu.</summary>
    internal static string? ContinueVerb(InProgressOperation operation) => operation switch
    {
        InProgressOperation.Merge => "merge",
        InProgressOperation.Rebase => "rebase",
        InProgressOperation.CherryPick => "cherry-pick",
        InProgressOperation.Revert => "revert",
        InProgressOperation.ApplyMailbox => "am",

        // Bisect'in `--continue`si yok; `git bisect reset` bambaşka bir şey.
        _ => null,
    };

    public async Task<ConflictProgress> GetProgressAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        InProgressOperation operation = await _operations
            .ReadAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        List<RepositoryPath> remaining = [];

        if (result.IsSuccess)
        {
            foreach (string value in result.GetStandardOutputText()
                         .Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (RepositoryPath.TryParse(value, out RepositoryPath path))
                {
                    remaining.Add(path);
                }
            }
        }

        return new ConflictProgress { Operation = operation, Remaining = remaining };
    }

    public Task MarkResolvedAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken = default) =>
        _writer.RunAsync(workingDirectory, ["add", "--", path.Value], cancellationToken);

    public Task RemoveAsync(
        string workingDirectory,
        RepositoryPath path,
        CancellationToken cancellationToken = default) =>
        _writer.RunAsync(workingDirectory, ["rm", "-q", "--", path.Value], cancellationToken);

    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ — <c>checkout --ours</c> çakışmayı TEMİZLEMİYOR.</b> İçeriği yazıyor
    /// ama dosya index'te hâlâ <c>U</c>; ardından <c>git add</c> gelmezse kullanıcı
    /// "çözdüm" sanır ve <c>--continue</c> reddedilir. İki adım burada birleştirildi.
    /// </remarks>
    public async Task TakeSideAsync(
        string workingDirectory,
        RepositoryPath path,
        ResolutionSide side,
        CancellationToken cancellationToken = default)
    {
        string flag = side == ResolutionSide.Ours ? "--ours" : "--theirs";

        await _writer
            .RunAsync(workingDirectory, ["checkout", flag, "--", path.Value], cancellationToken)
            .ConfigureAwait(false);

        await MarkResolvedAsync(workingDirectory, path, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteResolvedAsync(
        string workingDirectory,
        RepositoryPath path,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        await File.WriteAllBytesAsync(
            path.ToAbsolutePath(workingDirectory),
            content,
            cancellationToken).ConfigureAwait(false);

        await MarkResolvedAsync(workingDirectory, path, cancellationToken).ConfigureAwait(false);
    }

    public async Task ContinueAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        string verb = await ResolveVerbAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        await _writer.RunWithEnvironmentAsync(
            workingDirectory,
            [verb, "--continue"],
            NonInteractiveEditor,
            progress: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task AbortAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        string verb = await ResolveVerbAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        await _writer
            .RunAsync(workingDirectory, [verb, "--abort"], cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Arayüzün bir editör beklerken donmasını imkânsız kılan ortam.
    /// </summary>
    /// <remarks>
    /// <c>true</c> her zaman başarıyla ve hiçbir şey yazmadan çıkar; git bunu "kullanıcı
    /// mesajı değiştirmedi" diye yorumlar. Windows'ta <c>true</c> yok, bu yüzden
    /// <c>cmd /c exit 0</c> eşdeğeri kullanılıyor.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> NonInteractiveEditor =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_EDITOR"] = OperatingSystem.IsWindows() ? "cmd /c exit 0" : "true",
        };

    private async Task<string> ResolveVerbAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        InProgressOperation operation = await _operations
            .ReadAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        return ContinueVerb(operation)
            ?? throw new InvalidOperationException(
                "Sürdürülebilecek bir işlem yok; önce bir merge/rebase/cherry-pick başlamalı.");
    }
}
