using System.Diagnostics;
using GitExt.Core.Git;

namespace GitExt.Core.Tests;

/// <summary>
/// Flatpak sandbox'ında git'in host üzerinde çalıştırılmasını doğrular
/// (P10-T10, ADR-0009).
/// </summary>
/// <remarks>
/// Bu testlerin kapattığı boşluk, sarmalamanın <b>sessizce eksik</b> olmasıdır.
/// Kayıp bir çalışma dizini komutu yanlış depoya karşı çalıştırır; kayıp bir ortam
/// değişkeni git'i kullanıcının yapılandırması olmadan çalıştırır. İkisi de hata
/// vermez, yalnızca yanlış sonuç üretir.
/// </remarks>
public class SandboxLauncherTests
{
    private static ProcessStartInfo GitCommit(string workingDirectory = "/home/user/repo")
    {
        ProcessStartInfo info = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
        };

        info.ArgumentList.Add("commit");
        info.ArgumentList.Add("-m");
        info.ArgumentList.Add("mesaj içinde boşluk var");

        return info;
    }

    [Fact]
    public void Sandbox_disinda_hicbir_sey_degismiyor()
    {
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: false);

        info.FileName.ShouldBe("git");
        info.ArgumentList.ShouldBe(["commit", "-m", "mesaj içinde boşluk var"]);
    }

    [Fact]
    public void Sandbox_icinde_flatpak_spawn_ile_sarmalaniyor()
    {
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.FileName.ShouldBe("flatpak-spawn");
        info.ArgumentList[0].ShouldBe("--host");
    }

    [Fact]
    public void Calisma_dizini_argüman_olarak_aktariliyor()
    {
        // flatpak-spawn çağıran sürecin çalışma dizinini host tarafına TAŞIMIYOR.
        // Aktarılmazsa git host kullanıcısının ev dizininde çalışır — yani yanlış
        // depoya karşı. Hata vermez, sadece yanlış sonuç verir.
        ProcessStartInfo info = GitCommit("/home/user/projects/gitext-core");

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.ArgumentList.ShouldContain("--directory=/home/user/projects/gitext-core");
    }

    [Fact]
    public void Ortam_degiskenleri_aktariliyor()
    {
        // --host ile başlatılan süreç sandbox'ın ortamını DEVRALMIYOR. GitEnvironment'ın
        // kurduğu her şey (LC_ALL, GIT_* geçersiz kılmaları, askpass) aktarılmazsa
        // host'taki git bambaşka bir yapılandırmayla çalışır.
        ProcessStartInfo info = GitCommit();
        info.Environment["LC_ALL"] = "C";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.ArgumentList.ShouldContain("--env=LC_ALL=C");
        info.ArgumentList.ShouldContain("--env=GIT_TERMINAL_PROMPT=0");
    }

    [Fact]
    public void Komut_ve_argumanlari_sirasiyla_korunuyor()
    {
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        // Komut adı ve argümanları, bayrakların ARDINDAN ve kendi sıralarında gelmeli.
        int gitIndex = info.ArgumentList.IndexOf("git");
        gitIndex.ShouldBeGreaterThan(0);

        info.ArgumentList.Skip(gitIndex).ShouldBe(["git", "commit", "-m", "mesaj içinde boşluk var"]);
    }

    [Fact]
    public void Bosluk_iceren_arguman_tek_parca_kaliyor()
    {
        // Argümanlar ArgumentList üzerinden geçiyor, birleştirilmiş bir komut satırı
        // olarak değil. Birleştirilseydi boşluk içeren commit mesajları bölünür ve
        // git bunları ayrı argümanlar sanardı.
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.ArgumentList.ShouldContain("mesaj içinde boşluk var");
    }

    [Fact]
    public void Sarmalama_iki_kez_uygulanmiyor()
    {
        // İkinci kez sarmalamak "flatpak-spawn --host flatpak-spawn --host git" üretirdi:
        // çalışmaz ve hatası da anlaşılmaz olur. Sarmalayıcıların ikinci kez uygulanması
        // klasik bir kazadır, bu yüzden idempotent.
        ProcessStartInfo info = GitCommit();

        SandboxLauncher.RewriteForHost(info, sandboxed: true);
        List<string> afterFirst = [.. info.ArgumentList];

        SandboxLauncher.RewriteForHost(info, sandboxed: true);

        info.FileName.ShouldBe("flatpak-spawn");
        info.ArgumentList.ShouldBe(afterFirst);
        info.ArgumentList.Count(a => a == "--host").ShouldBe(1);
    }

    [Fact]
    public void Bu_makinede_sandbox_algilanmiyor()
    {
        // Testler bir Flatpak sandbox'ında koşmuyor; algılama yanlış pozitif verirse
        // her git çağrısı var olmayan flatpak-spawn'a yönlenirdi.
        SandboxLauncher.IsSandboxed.ShouldBeFalse();
    }

    [Fact]
    public void Sandbox_disinda_host_dogrulamasi_sorunsuz_geciyor()
    {
        // Sandbox dışında bu çağrı hiçbir şey yapmamalı — aksi halde flatpak-spawn'ı
        // olmayan her sistemde uygulama açılmazdı.
        Should.NotThrow(SandboxLauncher.EnsureHostAccessible);
    }
}
