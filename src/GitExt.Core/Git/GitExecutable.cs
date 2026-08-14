using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitExt.Core.Git;

/// <summary>
/// Bulunmuş ve sürümü doğrulanmış bir <c>git</c> çalıştırılabiliri.
/// </summary>
/// <remarks>
/// Bu tipin bir örneği varsa, <c>git</c> bulunmuş ve <see cref="GitVersion.Minimum"/> koşulunu
/// sağladığı doğrulanmış demektir. Bu doğrulamayı tip sistemine taşımak, "acaba git var mı"
/// kontrolünün her çağrı yerinde tekrarlanmasını önler.
/// </remarks>
public sealed class GitExecutable
{
    private GitExecutable(string path, GitVersion version)
    {
        Path = path;
        Version = version;
    }

    /// <summary>Çalıştırılabilirin tam yolu.</summary>
    public string Path { get; }

    /// <summary>Doğrulanmış sürüm.</summary>
    public GitVersion Version { get; }

    /// <summary>
    /// Sistemde <c>git</c> arar, sürümünü okur ve doğrular.
    /// </summary>
    /// <param name="explicitPath">
    /// Kullanıcı ayarlarından gelen açık yol. Verilirse arama yapılmaz.
    /// </param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    /// <exception cref="GitNotFoundException">Çalıştırılabilir bulunamadığında.</exception>
    /// <exception cref="GitVersionTooOldException">Sürüm çok eskiyse.</exception>
    public static async Task<GitExecutable> LocateAsync(
        string? explicitPath = null,
        CancellationToken cancellationToken = default)
    {
        // Sandbox içindeysek host'a erişimin gerçekten mümkün olduğu ÖNCE doğrulanıyor.
        // Erişilemiyorsa burada durulur; sandbox içindeki bir git'e sessizce düşmek,
        // hook'u olan depolarda commit'in sessizce atılmamasına yol açıyor (ADR-0009).
        SandboxLauncher.EnsureHostAccessible();

        List<string> attempted = [];

        foreach (string candidate in EnumerateCandidates(explicitPath))
        {
            attempted.Add(candidate);

            GitVersion? version = await TryReadVersionAsync(candidate, cancellationToken)
                .ConfigureAwait(false);

            if (version is not { } found)
            {
                continue;
            }

            if (found < GitVersion.Minimum)
            {
                throw new GitVersionTooOldException(found, candidate);
            }

            return new GitExecutable(candidate, found);
        }

        throw new GitNotFoundException(
            "Çalıştırılabilir bir 'git' bulunamadı. Git kurulu ve PATH üzerinde olmalı. "
            + $"Denenen yollar: {string.Join(", ", attempted)}");
    }

    /// <summary>
    /// Aday yolları, denenme sırasına göre üretir.
    /// </summary>
    private static IEnumerable<string> EnumerateCandidates(string? explicitPath)
    {
        // Kullanıcı açıkça bir yol verdiyse yalnızca onu dene — sessizce başka bir git'e
        // düşmek, teşhisi zor davranış farklarına yol açar.
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return explicitPath;
            yield break;
        }

        // PATH üzerinden: en yaygın ve kullanıcının beklediği durum.
        yield return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "git.exe" : "git";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Git for Windows varsayılan konumları. PATH'e eklenmeden kurulabiliyor.
            foreach (string root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     })
            {
                if (!string.IsNullOrEmpty(root))
                {
                    yield return System.IO.Path.Combine(root, "Git", "cmd", "git.exe");
                }
            }
        }
        else
        {
            // Homebrew (Apple Silicon ve Intel), Nix ve klasik Unix konumları.
            yield return "/opt/homebrew/bin/git";
            yield return "/usr/local/bin/git";
            yield return "/usr/bin/git";
        }
    }

    /// <summary>
    /// Adayı çalıştırıp sürümünü okur; çalıştırılamıyorsa <see langword="null"/> döner.
    /// </summary>
    private static async Task<GitVersion?> TryReadVersionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        startInfo.ArgumentList.Add("--version");
        startInfo.Environment["LC_ALL"] = "C";

        // Flatpak sandbox'ındaysak host'taki git aranıyor (ADR-0009). Sandbox içindeki
        // git'i bulmak, kullanıcının hook'larını ve yapılandırmasını göremeyen bir
        // git'i "bulundu" saymak olurdu.
        SandboxLauncher.RewriteForHost(startInfo);

        try
        {
            using Process process = new() { StartInfo = startInfo };

            if (!process.Start())
            {
                return null;
            }

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken)
                .ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return null;
            }

            return GitVersion.TryParse(output, out GitVersion version) ? version : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                       or FileNotFoundException
                                       or DirectoryNotFoundException)
        {
            // Aday yolda çalıştırılabilir yok — sıradakini dene.
            return null;
        }
    }
}
