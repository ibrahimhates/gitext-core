using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// Depoda süren çok adımlı bir işlem (P06-T04).
/// </summary>
public enum InProgressOperation
{
    /// <summary>Süren işlem yok.</summary>
    None,

    /// <summary>Rebase (interaktif veya değil).</summary>
    Rebase,

    /// <summary><c>git am</c> ile yama uygulanıyor.</summary>
    ApplyMailbox,

    /// <summary>Merge çakışmayla durdu.</summary>
    Merge,

    /// <summary>Cherry-pick sürüyor.</summary>
    CherryPick,

    /// <summary>Revert sürüyor.</summary>
    Revert,

    /// <summary>Bisect sürüyor.</summary>
    Bisect,
}

/// <summary>
/// Depoda süren işlemi okur (P06-T04).
/// </summary>
public interface IInProgressOperationReader
{
    Task<InProgressOperation> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Git dizinindeki durum dosyalarına bakarak süren işlemi belirler (P06-T04).
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden gerekli?</b> ÖLÇÜLDÜ: <b>rebase ve bisect sırasında HEAD ayrık</b>
/// (<c>symbolic-ref</c> çıkış kodu 1, <c>--porcelain=v2</c> <c>(detached)</c> diyor).
/// Düz bir "ayrık HEAD" uyarısı bu iki durumda da açılırdı; oysa kullanıcı bilerek bir
/// işlemin ortasında ve ona söylenmesi gereken şey "buradan dal oluştur" değil,
/// <b>hangi işlemin sürdüğü</b>.
/// </para>
/// <para>
/// Dosya adları <see cref="RepositoryChangeClassifier"/>'ın izlediği adlarla aynı — orada
/// "depo durumu değişti" sayılan şeyler tam olarak bunlar (P05-T14).
/// </para>
/// <para>
/// ⚠️ Yol <c>--absolute-git-dir</c> ile alınıyor: <c>--git-path</c> <b>göreli</b> dönüyor ve
/// çalışma dizinine bağlı (P05-T13, madde 20e). Bağlı çalışma ağacında da doğru olan bu —
/// bu dosyalar ortak dizinde değil, o worktree'nin <b>kendi</b> dizininde duruyor.
/// </para>
/// </remarks>
public sealed class InProgressOperationReader : IInProgressOperationReader
{
    private readonly IGitProcessRunner _runner;

    public InProgressOperationReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<InProgressOperation> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--absolute-git-dir"],
            },
            cancellationToken).ConfigureAwait(false);

        string gitDirectory = result.GetStandardOutputText().Trim();

        return gitDirectory.Length == 0 ? InProgressOperation.None : Classify(gitDirectory);
    }

    /// <summary>Git dizinindeki durum dosyalarına bakar.</summary>
    /// <remarks>
    /// Sıra önemli: rebase sırasında <c>MERGE_HEAD</c> de oluşabiliyor, ama kullanıcı için
    /// asıl bağlam rebase'dir.
    /// </remarks>
    internal static InProgressOperation Classify(string gitDirectory)
    {
        if (Directory.Exists(Path.Combine(gitDirectory, "rebase-merge")))
        {
            return InProgressOperation.Rebase;
        }

        // `rebase-apply` hem `rebase --apply` hem `git am` tarafından kullanılıyor;
        // ayrımı içindeki `applying` dosyası veriyor.
        string applyDirectory = Path.Combine(gitDirectory, "rebase-apply");

        if (Directory.Exists(applyDirectory))
        {
            return File.Exists(Path.Combine(applyDirectory, "applying"))
                ? InProgressOperation.ApplyMailbox
                : InProgressOperation.Rebase;
        }

        if (File.Exists(Path.Combine(gitDirectory, "CHERRY_PICK_HEAD")))
        {
            return InProgressOperation.CherryPick;
        }

        if (File.Exists(Path.Combine(gitDirectory, "REVERT_HEAD")))
        {
            return InProgressOperation.Revert;
        }

        if (File.Exists(Path.Combine(gitDirectory, "MERGE_HEAD")))
        {
            return InProgressOperation.Merge;
        }

        return File.Exists(Path.Combine(gitDirectory, "BISECT_LOG"))
            ? InProgressOperation.Bisect
            : InProgressOperation.None;
    }
}
