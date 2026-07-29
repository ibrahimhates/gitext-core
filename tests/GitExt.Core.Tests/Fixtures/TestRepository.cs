using System.Diagnostics;
using System.Text;

namespace GitExt.Core.Tests.Fixtures;

/// <summary>
/// P02-T13 — Testler için <b>gerçek <c>git</c></b> ile geçici depo oluşturur.
/// </summary>
/// <remarks>
/// Elle yazılmış sahte <c>git</c> çıktısına karşı test yazmak, ayrıştırıcıyı değil bizim
/// çıktının nasıl göründüğüne dair varsayımımızı test eder. Bu yüzden fixture'lar gerçek
/// <c>git</c> çalıştırılarak üretilir (ADR-0003).
/// </remarks>
public sealed class TestRepository : IDisposable
{
    private readonly string _root;
    private bool _disposed;

    private TestRepository(string root)
    {
        _root = root;
    }

    /// <summary>Deponun çalışma dizini.</summary>
    public string Path => _root;

    /// <summary>
    /// Boş bir depo oluşturur (<c>git init</c>, hiç commit yok).
    /// </summary>
    public static TestRepository CreateEmpty()
    {
        string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "gitext-test-" + Guid.NewGuid().ToString("N")[..12]);

        Directory.CreateDirectory(root);
        TestRepository repository = new(root);

        // --initial-branch: git'in varsayılan dal adı sürüme ve kullanıcı yapılandırmasına
        // göre değişiyor (master/main). Testler deterministik olmalı.
        repository.Git("init", "--initial-branch=main");

        // Commit atabilmek için kimlik gerekli. Yerel yapılandırma, kullanıcının global
        // ayarlarından etkilenmemek için --local.
        repository.Git("config", "--local", "user.name", "gitext-core tests");
        repository.Git("config", "--local", "user.email", "tests@gitext-core.invalid");
        repository.Git("config", "--local", "commit.gpgsign", "false");
        repository.Git("config", "--local", "core.autocrlf", "false");

        return repository;
    }

    /// <summary>
    /// Tek commit içeren basit bir depo oluşturur.
    /// </summary>
    public static TestRepository CreateWithSingleCommit()
    {
        TestRepository repository = CreateEmpty();
        repository.WriteFile("README.md", "# test\n");
        repository.Git("add", "README.md");
        repository.Commit("ilk commit");
        return repository;
    }

    /// <summary>
    /// Deponun içinde bir dosya oluşturur veya üzerine yazar.
    /// </summary>
    public void WriteFile(string relativePath, string content)
    {
        string full = System.IO.Path.Combine(_root, relativePath);
        string? directory = System.IO.Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // LF sabitlenir: CRLF farkı diff ve yama testlerini sessizce bozar.
        File.WriteAllText(full, content.ReplaceLineEndings("\n"), new UTF8Encoding(false));
    }

    /// <summary>
    /// Commit atar. Mesaj stdin üzerinden geçirilir — komut satırına gömülmez.
    /// </summary>
    public void Commit(string message)
    {
        Git("commit", "--allow-empty", "-m", message);
    }

    /// <summary>
    /// Depoya bir hook kurar.
    /// </summary>
    /// <param name="name">Hook adı, örn. <c>pre-commit</c>.</param>
    /// <param name="shellScript">Kabuk betiği gövdesi (shebang olmadan).</param>
    /// <remarks>
    /// Windows'ta da çalışır: Git for Windows hook'ları kendi bundled <c>sh</c>'ı ile yürütür.
    /// </remarks>
    public void InstallHook(string name, string shellScript)
    {
        string hooksDirectory = System.IO.Path.Combine(_root, ".git", "hooks");
        Directory.CreateDirectory(hooksDirectory);

        string hookPath = System.IO.Path.Combine(hooksDirectory, name);
        File.WriteAllText(
            hookPath,
            ("#!/bin/sh\n" + shellScript).ReplaceLineEndings("\n"),
            new UTF8Encoding(false));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                hookPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>
    /// Depoda bir <c>git</c> komutu çalıştırır ve stdout'unu döndürür.
    /// </summary>
    /// <remarks>
    /// Bu, test kurulumu içindir ve kasıtlı olarak <see cref="Core.Git.IGitProcessRunner"/>
    /// kullanmaz — test ettiğimiz şeyi fixture kurmak için kullanmak, ikisinin aynı hatayı
    /// paylaşması durumunda testi işe yaramaz hale getirir.
    /// </remarks>
    public string Git(params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = _root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        // Kullanıcının ~/.gitconfig'i testleri etkilemesin (hook'lar, template'ler, imzalama).
        startInfo.Environment["HOME"] = _root;
        startInfo.Environment["XDG_CONFIG_HOME"] = _root;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git süreci başlatılamadı.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Fixture komutu başarısız: git {string.Join(' ', arguments)}{Environment.NewLine}{stderr}");
        }

        return stdout;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // .git altındaki nesne dosyaları salt okunur olabilir; silmeden önce izinleri aç.
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Geçici dizin temizlenemedi. Testi bu yüzden kırmak, gerçek bir hatayı
            // maskelemekten daha zararlı olur; işletim sistemi er geç temizler.
        }
    }
}
