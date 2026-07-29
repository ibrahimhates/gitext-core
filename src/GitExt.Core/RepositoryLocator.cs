using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Bir dosya sistemi yolundan Git deposu keşfeder (P02-T06).
/// </summary>
public interface IRepositoryLocator
{
    /// <summary>
    /// Verilen yolu içeren depoyu bulur.
    /// </summary>
    /// <remarks>
    /// Yol deponun alt dizinlerinden biri olabilir; git yukarı doğru arar.
    /// </remarks>
    /// <exception cref="GitException">
    /// Yol bir depo değilse <see cref="GitFailureKind.NotARepository"/> ile.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">Dizin mevcut değilse.</exception>
    Task<RepositoryLocation> LocateAsync(string path, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRepositoryLocator"/>
public sealed class RepositoryLocator : IRepositoryLocator
{
    private readonly IGitProcessRunner _runner;

    public RepositoryLocator(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<RepositoryLocation> LocateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string directory = Path.GetFullPath(path);

        if (File.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory)
                        ?? throw new DirectoryNotFoundException($"Dizin belirlenemedi: {path}");
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Dizin bulunamadı: {directory}");
        }

        // Tek çağrıda alınabilecek her şey. --show-toplevel BURADA OLAMAZ:
        // bare depoda "fatal: this operation must be run in a work tree" ile 128 döner
        // ve tüm çağrıyı kırar. Gerçek git ile doğrulandı.
        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(
                directory,
                "rev-parse",
                "--absolute-git-dir",
                "--git-common-dir",
                "--is-bare-repository"),
            cancellationToken).ConfigureAwait(false);

        string[] lines = SplitLines(result.GetStandardOutputText());

        if (lines.Length < 3)
        {
            throw new GitException(
                GitFailureKind.Unknown,
                "git rev-parse beklenen alanları döndürmedi.",
                result.Command.ToDisplayString(),
                result.ExitCode,
                result.StandardError);
        }

        string gitDirectory = Path.GetFullPath(lines[0]);

        // --git-common-dir normal bir depoda GÖRELİ döner (".git"), worktree'de mutlak.
        // --path-format=absolute bunu çözerdi ama git 2.31+ gerektiriyor; minimumumuz 2.30.
        // Bu yüzden çalışma dizinine göre kendimiz çözüyoruz.
        string commonDirectory = Path.GetFullPath(lines[1], directory);

        bool isBare = string.Equals(lines[2], "true", StringComparison.OrdinalIgnoreCase);

        string? workTreeRoot = isBare
            ? null
            : await ReadWorkTreeRootAsync(directory, cancellationToken).ConfigureAwait(false);

        string? superproject = isBare
            ? null
            : await ReadSuperprojectAsync(directory, cancellationToken).ConfigureAwait(false);

        return new RepositoryLocation(gitDirectory, commonDirectory, workTreeRoot, superproject);
    }

    private async Task<string> ReadWorkTreeRootAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        string root = await _runner.RunForTextAsync(
            GitCommand.Create(directory, "rev-parse", "--show-toplevel"),
            cancellationToken).ConfigureAwait(false);

        return Path.GetFullPath(root);
    }

    /// <summary>
    /// Depo bir submodule ise üst projenin çalışma ağacını döndürür.
    /// </summary>
    /// <remarks>
    /// <c>--show-superproject-working-tree</c> submodule değilse <b>boş çıktı ve 0</b> döner —
    /// hata değil. Bu yüzden boşluk kontrolü yeterli.
    /// </remarks>
    private async Task<string?> ReadSuperprojectAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        string superproject = await _runner.RunForTextAsync(
            GitCommand.Create(directory, "rev-parse", "--show-superproject-working-tree"),
            cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(superproject) ? null : Path.GetFullPath(superproject);
    }

    private static string[] SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
