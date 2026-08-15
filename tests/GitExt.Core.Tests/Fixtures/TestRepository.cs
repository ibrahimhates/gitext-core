using System.Diagnostics;
using System.Text;

namespace GitExt.Core.Tests.Fixtures;

/// <summary>
/// P02-T13 — Creates a temporary repository with <b>real <c>git</c></b> for the tests.
/// </summary>
/// <remarks>
/// Writing tests against hand-written fake <c>git</c> output tests our assumption about how the
/// output looks, not the parser. That is why the fixtures are produced by running real
/// <c>git</c> (ADR-0003).
/// </remarks>
public sealed class TestRepository : IDisposable
{
    private readonly string _root;
    private bool _disposed;

    private TestRepository(string root)
    {
        _root = root;
    }

    /// <summary>The repository's working directory.</summary>
    public string Path => _root;

    private static string NewTemporaryDirectory() =>
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "gitext-test-" + Guid.NewGuid().ToString("N")[..12]);

    /// <summary>
    /// Creates an empty repository (<c>git init</c>, no commits at all).
    /// </summary>
    public static TestRepository CreateEmpty()
    {
        string root = NewTemporaryDirectory();
        Directory.CreateDirectory(root);
        TestRepository repository = new(root);

        // --initial-branch: git's default branch name varies with the version and the user's
        // configuration (master/main). Tests must be deterministic.
        repository.Git("init", "--initial-branch=main");

        // An identity is required to be able to commit. Local configuration, with --local, so as not to
        // be affected by the user's global settings.
        repository.Git("config", "--local", "user.name", "gitext-core tests");
        repository.Git("config", "--local", "user.email", "tests@gitext-core.invalid");
        repository.Git("config", "--local", "commit.gpgsign", "false");
        repository.Git("config", "--local", "core.autocrlf", "false");

        return repository;
    }

    /// <summary>
    /// Creates a simple repository containing a single commit.
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
    /// Creates a file inside the repository or overwrites it.
    /// </summary>
    public void WriteFile(string relativePath, string content)
    {
        string full = System.IO.Path.Combine(_root, relativePath);
        string? directory = System.IO.Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // LF is pinned: a CRLF difference silently breaks the diff and patch tests.
        File.WriteAllText(full, content.ReplaceLineEndings("\n"), new UTF8Encoding(false));
    }

    /// <summary>
    /// Makes a commit. The message is passed via stdin — it is not embedded in the command line.
    /// </summary>
    public void Commit(string message)
    {
        Git("commit", "--allow-empty", "-m", message);
    }

    /// <summary>
    /// Makes a commit with the given date.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <param name="isoDate">ISO-8601 date, e.g. <c>2020-01-01T00:00:00</c>.</param>
    /// <remarks>
    /// For simulating skewed histories (rebase, import, clock drift).
    /// The date is set for both the author and the committer — git uses the <b>committer</b> date for
    /// ordering, so setting only the author date is not enough.
    /// </remarks>
    public void CommitAtDate(string message, string isoDate)
    {
        // git uses the COMMITTER date for ordering. Because `--date` sets only the author date, it is
        // not enough on its own; the committer date is given via an environment variable.
        GitWithEnvironment(
            new Dictionary<string, string>
            {
                ["GIT_AUTHOR_DATE"] = isoDate,
                ["GIT_COMMITTER_DATE"] = isoDate,
            },
            "commit", "--allow-empty", "-m", message);
    }

    /// <summary>
    /// Creates a repository with no working tree (bare).
    /// </summary>
    public static TestRepository CreateBare()
    {
        string root = NewTemporaryDirectory();
        Directory.CreateDirectory(root);

        TestRepository repository = new(root);
        repository.Git("init", "--bare", "--initial-branch=main");
        return repository;
    }

    /// <summary>
    /// Adds a linked worktree to this repository and returns the object representing it.
    /// </summary>
    /// <remarks>
    /// The returned object does <b>not take ownership</b>: the worktree directory does not go away with
    /// this repository's cleanup, it must be disposed separately.
    /// </remarks>
    public TestRepository AddWorkTree(string branchName)
    {
        string worktreePath = NewTemporaryDirectory();
        Git("worktree", "add", "-b", branchName, worktreePath);
        return new TestRepository(worktreePath);
    }

    /// <summary>
    /// Adds another repository to this one as a submodule.
    /// </summary>
    public void AddSubmodule(TestRepository other, string relativePath)
    {
        // protocol.file.allow: since git 2.38.1 adding a submodule from a local file path is forbidden
        // by default (CVE-2022-39253). We enable it deliberately in the tests.
        Git("-c", "protocol.file.allow=always", "submodule", "add", "--", other.Path, relativePath);
    }

    /// <summary>
    /// Enables SSH signing in this repository and makes signed commits possible.
    /// </summary>
    /// <remarks>
    /// SSH signing is used instead of GPG: key generation is non-interactive and fast, and no GPG
    /// keyring setup is needed. The structure of the signature in the commit object is the same for both
    /// (a <c>gpgsig</c> header, multi-line).
    /// </remarks>
    /// <returns><see langword="true"/> if signing could be set up.</returns>
    public bool TryEnableSshSigning()
    {
        string keyPath = System.IO.Path.Combine(_root, "imza-anahtari");

        try
        {
            using Process keygen = Process.Start(new ProcessStartInfo("ssh-keygen")
            {
                ArgumentList = { "-q", "-t", "ed25519", "-N", string.Empty, "-f", keyPath, "-C", "test@invalid" },
                UseShellExecute = false,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("ssh-keygen başlatılamadı.");

            keygen.WaitForExit();

            if (keygen.ExitCode != 0)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No ssh-keygen (on some Windows installations). The test is skipped.
            return false;
        }

        Git("config", "--local", "gpg.format", "ssh");
        Git("config", "--local", "user.signingkey", keyPath + ".pub");
        return true;
    }

    /// <summary>
    /// Adds the signing key to the list of allowed signers.
    /// </summary>
    /// <remarks>
    /// <b>MEASURED:</b> without this git returns <c>N</c> — i.e. "unsigned" — in the <c>%G?</c> field
    /// for a signed commit and only writes the error to stderr. In other words
    /// <see cref="TryEnableSshSigning"/> on its own does not produce the "valid signature" scenario.
    /// </remarks>
    public void TrustSigningKey()
    {
        string keyPath = System.IO.Path.Combine(_root, "imza-anahtari");
        string allowedSigners = System.IO.Path.Combine(_root, "allowed-signers");

        File.WriteAllText(
            allowedSigners,
            $"tests@gitext-core.invalid {File.ReadAllText(keyPath + ".pub").Trim()}\n");

        Git("config", "--local", "gpg.ssh.allowedSignersFile", allowedSigners);
    }

    /// <summary>
    /// Installs a hook into the repository.
    /// </summary>
    /// <param name="name">The hook name, e.g. <c>pre-commit</c>.</param>
    /// <param name="shellScript">The shell script body (without the shebang).</param>
    /// <remarks>
    /// Works on Windows too: Git for Windows executes hooks with its own bundled <c>sh</c>.
    /// </remarks>
    public void InstallHook(string name, string shellScript)
    {
        // In a bare repository the hooks are NOT under `.git/hooks` but at the repository root; the
        // location is asked of git itself (in P06-T08 the remote-side hook is installed into a bare repository).
        string hooksDirectory = System.IO.Path.GetFullPath(
            Git("rev-parse", "--git-path", "hooks").Trim(),
            _root);

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
    /// Runs a <c>git</c> command in the repository and returns its stdout.
    /// </summary>
    /// <remarks>
    /// This is for test setup and deliberately does not use <see cref="Core.Git.IGitProcessRunner"/> —
    /// using the very thing we are testing to set up the fixture makes the test useless if the two
    /// share the same bug.
    /// </remarks>
    public string Git(params string[] arguments) =>
        GitWithEnvironment(null, arguments);

    /// <summary>
    /// Runs a <c>git</c> command and returns its output <b>losslessly</b>.
    /// </summary>
    /// <remarks>
    /// Every byte corresponds one-to-one to a character. <c>DiffParser</c> expects its input in this
    /// form: the diff output is not in a single encoding, the line contents are the file's own
    /// bytes (see <c>GitResult.GetStandardOutputLossless</c>).
    /// </remarks>
    public string GitLossless(params string[] arguments)
    {
        ProcessStartInfo startInfo = CreateStartInfo(null, arguments);

        // 🔴 Caught in P05-T16. The previous version first converted the output to a string with UTF-8;
        // every non-ASCII byte turns into U+FFFD there and converting back to Latin1 does NOT REPAIR it.
        // So despite the name "lossless", output with Latin-5 content was silently corrupted — because
        // the test's measuring instrument was broken, correct code looked buggy.
        // The fix is to read the output with Latin1 FROM THE START: byte ↔ character one-to-one.
        startInfo.StandardOutputEncoding = System.Text.Encoding.Latin1;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git süreci başlatılamadı.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} başarısız (çıkış {process.ExitCode}): {stderr}");
        }

        return stdout;
    }

    /// <summary>
    /// Runs a <c>git</c> command and <b>does not throw on failure</b>.
    /// </summary>
    /// <remarks>
    /// For tests that measure <i>expected</i> errors such as a lock collision (P05-T01):
    /// <see cref="Git"/> cannot be used there because it throws.
    /// </remarks>
    public (int ExitCode, string Error) TryGit(params string[] arguments)
    {
        ProcessStartInfo startInfo = CreateStartInfo(null, arguments);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git süreci başlatılamadı.");

        _ = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stderr);
    }

    /// <summary>
    /// Runs a <c>git</c> command with additional environment variables.
    /// </summary>
    public string GitWithEnvironment(
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = CreateStartInfo(environment, arguments);

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

    private ProcessStartInfo CreateStartInfo(
        IReadOnlyDictionary<string, string>? environment,
        string[] arguments)
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
        // Keep the user's ~/.gitconfig from affecting the tests (hooks, templates, signing).
        startInfo.Environment["HOME"] = _root;
        startInfo.Environment["XDG_CONFIG_HOME"] = _root;

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
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
            // Object files under .git can be read-only; open the permissions before deleting.
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // The temporary directory could not be cleaned up. Breaking the test for that would do more
            // harm than masking a real bug; the operating system will clean it up sooner or later.
        }
    }
}
