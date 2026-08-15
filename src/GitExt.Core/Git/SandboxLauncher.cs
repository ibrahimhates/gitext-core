using System.Diagnostics;

namespace GitExt.Core.Git;

/// <summary>
/// Uygulama bir Flatpak sandbox'ında çalışıyorsa <c>git</c>'i host üzerinde
/// çalıştırmayı sağlar (P10-T10, ADR-0009).
/// </summary>
/// <remarks>
/// <para>
/// ADR-0002'nin tüm gerekçesi <b>kullanıcının kendi git'ine</b> ulaşmaktır: hook'lar,
/// kimlik bilgisi yardımcıları, LFS, <c>.gitconfig</c>. Sandbox içindeki bir git bunların
/// hiçbirini göremez.
/// </para>
/// <para>
/// 🔴 <b>Ölçüldü (P10-T10):</b> <c>git</c> olan ama <c>python3</c> olmayan bir ortamda —
/// yani git'i gömen bir runtime'ın tam karşılığında — Python ile yazılmış bir
/// <c>pre-commit</c> hook'u olan depoda <c>git commit</c> çalıştırıldığında:
/// <b>commit atılmadı, ama çıkış kodu 0 döndü.</b> Tek belirti <c>env</c>'in stderr'e
/// yazdığı bir satırdı. Arayüz "commit edildi" derdi ve kullanıcı bunu ancak sonradan —
/// hiçbir şey göndermeyen bir push'ta — fark ederdi.
/// </para>
/// <para>
/// Bu yüzden sandbox içinde git <b>host üzerinden</b> çalıştırılıyor. <c>flatpak-spawn</c>
/// yoksa uygulama <b>yüksek sesle</b> başarısız oluyor; sandbox içindeki bir git'e sessizce
/// düşmek, yukarıdaki hatayı geri getirirdi.
/// </para>
/// </remarks>
public static class SandboxLauncher
{
    /// <summary>
    /// Flatpak'in sandbox içinde her zaman oluşturduğu bilgi dosyası.
    /// </summary>
    /// <remarks>
    /// Ortam değişkeni (<c>FLATPAK_ID</c>) değil bu dosya kullanılıyor: ortam değişkenleri
    /// alt süreçlere geçerken temizlenebiliyor ve kullanıcı tarafından taklit edilebiliyor.
    /// Dosyanın varlığı sandbox'ın kendisi tarafından garanti ediliyor.
    /// </remarks>
    private const string FlatpakInfoPath = "/.flatpak-info";

    private const string SpawnExecutable = "flatpak-spawn";

    /// <summary>
    /// Uygulama bir Flatpak sandbox'ı içinde mi çalışıyor?
    /// </summary>
    public static bool IsSandboxed { get; } = File.Exists(FlatpakInfoPath);

    /// <summary>
    /// Sandbox içindeyken bir süreci host üzerinde çalıştıracak biçimde yeniden yazar.
    /// Sandbox dışındaysa <paramref name="startInfo"/> olduğu gibi bırakılır.
    /// </summary>
    /// <remarks>
    /// Çalışma dizini argüman olarak veriliyor: <c>flatpak-spawn</c> çağıran sürecin
    /// çalışma dizinini host tarafına taşımıyor, host'taki süreç kendi dizininde başlıyor.
    /// Bu atlandığında komutlar yanlış depoya karşı çalışırdı.
    /// </remarks>
    public static void RewriteForHost(ProcessStartInfo startInfo) =>
        RewriteForHost(startInfo, IsSandboxed);

    /// <summary>
    /// Sarmalamanın kendisi; sandbox durumu dışarıdan verilebiliyor.
    /// </summary>
    /// <remarks>
    /// Test edilebilirlik için ayrıldı: gerçek bir Flatpak sandbox'ı kurmadan
    /// sarmalamanın doğruluğu doğrulanabilmeli. Sarmalama yanlışsa — eksik ortam
    /// değişkeni, kayıp çalışma dizini — sonuç sessizce yanlış depoya veya yanlış
    /// yapılandırmaya karşı çalışan git olurdu.
    /// </remarks>
    internal static void RewriteForHost(ProcessStartInfo startInfo, bool sandboxed)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (!sandboxed)
        {
            return;
        }

        // Zaten sarmalanmışsa tekrar sarmalama: "flatpak-spawn --host flatpak-spawn
        // --host git" çalışmaz ve hatası da anlaşılmaz olur. Çağrı noktası bugün tek,
        // ama bu tür bir sarmalayıcının ikinci kez uygulanması klasik bir kazadır.
        if (string.Equals(startInfo.FileName, SpawnExecutable, StringComparison.Ordinal))
        {
            return;
        }

        List<string> hostArguments = ["--host"];

        // Ortam değişkenleri de açıkça aktarılmalı: --host ile başlatılan süreç
        // sandbox'ın ortamını DEVRALMIYOR. GitEnvironment'ın kurduğu her şey
        // (LC_ALL, GIT_* geçersiz kılmaları, kimlik doğrulama değişkenleri) burada
        // aktarılmazsa host'taki git bambaşka bir yapılandırmayla çalışır.
        foreach ((string name, string? value) in startInfo.Environment)
        {
            if (value is not null)
            {
                hostArguments.Add($"--env={name}={value}");
            }
        }

        if (!string.IsNullOrEmpty(startInfo.WorkingDirectory))
        {
            hostArguments.Add($"--directory={startInfo.WorkingDirectory}");
        }

        hostArguments.Add(startInfo.FileName);
        hostArguments.AddRange(startInfo.ArgumentList);

        startInfo.FileName = SpawnExecutable;
        startInfo.ArgumentList.Clear();

        foreach (string argument in hostArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    /// <summary>
    /// Sandbox içindeyken host'a erişimin gerçekten mümkün olduğunu doğrular.
    /// </summary>
    /// <exception cref="GitNotFoundException">
    /// Sandbox içindeyiz ama <c>flatpak-spawn</c> çalışmıyorsa.
    /// </exception>
    /// <remarks>
    /// Başlangıçta bir kez çağrılıyor. Sessizce sandbox içindeki bir git'e düşmek
    /// yerine burada durulması bilinçli: ADR-0009, sessiz geri düşüşü açıkça yasaklıyor.
    /// </remarks>
    public static void EnsureHostAccessible()
    {
        if (!IsSandboxed)
        {
            return;
        }

        ProcessStartInfo probe = new()
        {
            FileName = SpawnExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        probe.ArgumentList.Add("--host");
        probe.ArgumentList.Add("true");

        try
        {
            using Process process = Process.Start(probe)
                ?? throw new GitNotFoundException(SandboxFailureMessage);

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new GitNotFoundException(SandboxFailureMessage);
            }
        }
        catch (Exception ex) when (ex is not GitNotFoundException)
        {
            throw new GitNotFoundException(SandboxFailureMessage, ex);
        }
    }

    private const string SandboxFailureMessage =
        "Running inside a Flatpak sandbox but the host 'git' is unreachable "
        + "(flatpak-spawn --host failed). gitext-core has to use your git so that your hooks and "
        + "credential helpers keep working "
        + "(ADR-0002, ADR-0009). Required permission: --talk-name=org.freedesktop.Flatpak. "
        + "We do not fall back to a git inside the sandbox: it was measured that in repositories with hooks the commit "
        + "is silently not made while the exit code is still 0.";
}
