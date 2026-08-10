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

        if (isBare)
        {
            return new RepositoryLocation(gitDirectory, commonDirectory, null, null);
        }

        (string workTreeRoot, string? superproject) =
            await ReadWorkTreeAsync(directory, cancellationToken).ConfigureAwait(false);

        return new RepositoryLocation(gitDirectory, commonDirectory, workTreeRoot, superproject);
    }

    /// <summary>
    /// Çalışma ağacının kökünü ve — depo bir submodule ise — üst projenin ağacını okur.
    /// </summary>
    /// <remarks>
    /// <para>
    /// İkisi <b>tek çağrıda</b> alınıyor (P09-T06). Ayrı ayrı sorulduklarında depo açılışı
    /// iki yerine üç süreç başlatıyordu; süreç başlatma maliyeti Linux'ta birkaç ms, ama
    /// Windows'ta kat kat yüksek ve ADR-0002'nin bilinen zayıflığı tam olarak bu.
    /// </para>
    /// <para>
    /// ⚠️ Birleştirmenin çalışması <c>--show-superproject-working-tree</c>'nin submodule
    /// olmayan bir depoda <b>hiçbir satır basmamasına</b> dayanıyor — hata değil, boş
    /// çıktı ve 0. Dolayısıyla satır sayısı ayrımı yapıyor: bir satır varsa yalnızca kök,
    /// iki satır varsa kök + üst proje. Gerçek git ile her iki durumda da ölçüldü.
    /// </para>
    /// <para>
    /// <c>--show-toplevel</c> yukarıdaki ilk çağrıya eklenemiyor: bare depoda tüm çağrıyı
    /// 128 ile kırıyor. Bu yüzden iki çağrı üçe değil ikiye iniyor, bire değil.
    /// </para>
    /// </remarks>
    private async Task<(string WorkTreeRoot, string? Superproject)> ReadWorkTreeAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        string output = await _runner.RunForTextAsync(
            GitCommand.Create(
                directory,
                "rev-parse",
                "--show-toplevel",
                "--show-superproject-working-tree"),
            cancellationToken).ConfigureAwait(false);

        string[] lines = SplitLines(output);

        if (lines.Length == 0)
        {
            throw new GitException(
                GitFailureKind.Unknown,
                "git rev-parse çalışma ağacının kökünü döndürmedi.",
                "git rev-parse --show-toplevel --show-superproject-working-tree",
                exitCode: 0,
                standardError: string.Empty);
        }

        string root = Path.GetFullPath(lines[0]);

        string? superproject = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1])
            ? Path.GetFullPath(lines[1])
            : null;

        return (root, superproject);
    }

    private static string[] SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
