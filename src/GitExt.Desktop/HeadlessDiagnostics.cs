using System.Globalization;
using GitExt.Core;
using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Desktop;

/// <summary>
/// Runs <c>GitExt.Core</c> against a repository without opening the UI (P02-T16).
/// </summary>
/// <remarks>
/// <para>
/// It serves two purposes: showing that the core layer works correctly against real repositories
/// without a UI, and diagnosing "it does not work for me" reports coming from users.
/// </para>
/// <para>
/// Usage: <c>gitext-core --headless [repository-path]</c>
/// </para>
/// </remarks>
internal static class HeadlessDiagnostics
{
    internal const string Flag = "--headless";

    internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                      ?? Directory.GetCurrentDirectory();

        try
        {
            GitExecutable executable = await GitExecutable.LocateAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"git         : {executable.Version} ({executable.Path})");

            InMemoryGitCommandLog log = new();
            GitProcessRunner runner = new(executable, log);

            RepositoryLocation location = await new RepositoryLocator(runner)
                .LocateAsync(path, cancellationToken).ConfigureAwait(false);

            PrintLocation(location);

            string workingDirectory = location.WorkingDirectory;

            await PrintHeadAndRefsAsync(runner, workingDirectory, cancellationToken).ConfigureAwait(false);
            await PrintStatusAsync(runner, workingDirectory, cancellationToken).ConfigureAwait(false);
            await PrintRecentCommitsAsync(runner, workingDirectory, cancellationToken).ConfigureAwait(false);

            PrintCommandLog(log);
            return 0;
        }
        catch (Exception ex) when (ex is GitException or GitNotFoundException
                                       or GitVersionTooOldException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"HATA: {ex.Message}");

            if (ex is GitException git)
            {
                Console.Error.WriteLine($"  tür    : {git.Kind}");
                Console.Error.WriteLine($"  komut  : {git.CommandLine}");

                if (!string.IsNullOrWhiteSpace(git.StandardError))
                {
                    Console.Error.WriteLine($"  stderr : {git.StandardError.Trim()}");
                }
            }

            return 1;
        }
    }

    private static void PrintLocation(RepositoryLocation location)
    {
        Console.WriteLine($"depo        : {location.WorkingDirectory}");
        Console.WriteLine($"  git dizini: {location.GitDirectory}");

        if (location.IsLinkedWorkTree)
        {
            Console.WriteLine($"  ortak dizin: {location.CommonDirectory}  (bağlı worktree)");
        }

        if (location.IsBare)
        {
            Console.WriteLine("  tür       : bare (çalışma ağacı yok)");
        }

        if (location.IsSubmodule)
        {
            Console.WriteLine($"  üst proje : {location.SuperprojectWorkTree}");
        }
    }

    private static async Task PrintHeadAndRefsAsync(
        IGitProcessRunner runner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        RepositoryRefs refs = await new RefReader(runner)
            .ReadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"HEAD        : {refs.Head}");

        if (refs.CurrentBranch is { } current && current.Upstream is { } upstream)
        {
            string tracking = current.Tracking.IsGone
                ? "upstream silinmiş"
                : $"+{current.Tracking.Ahead} -{current.Tracking.Behind}";

            Console.WriteLine($"  upstream  : {upstream} ({tracking})");
        }

        Console.WriteLine(
            $"ref'ler     : {refs.LocalBranches.Count} yerel dal, "
            + $"{refs.RemoteBranches.Count} uzak dal, {refs.Tags.Count} tag, "
            + $"{refs.Remotes.Count} remote");

        foreach (RemoteInfo remote in refs.Remotes)
        {
            Console.WriteLine($"  {remote.Name,-10}: {remote.FetchUrl}");
        }
    }

    private static async Task PrintStatusAsync(
        IGitProcessRunner runner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        WorkingTreeStatus status = await new StatusReader(runner)
            .ReadAsync(workingDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);

        Console.WriteLine();

        if (status.IsClean)
        {
            Console.WriteLine("durum       : temiz");
            return;
        }

        Console.WriteLine(
            $"durum       : {status.Staged.Count()} staged, {status.Unstaged.Count()} unstaged, "
            + $"{status.Untracked.Count()} untracked, {status.Conflicted.Count()} çakışma");

        foreach (FileStatus entry in status.Entries.Take(20))
        {
            string marker = entry switch
            {
                { IsConflicted: true } => $"!! {entry.Conflict}",
                { IsUntracked: true } => "?? untracked",
                _ => $"{entry.StagedChange}/{entry.UnstagedChange}",
            };

            string rename = entry.OriginalPath is { } original
                ? $"  ← {original.Value}"
                : string.Empty;

            Console.WriteLine($"  {marker,-24} {entry.Path.Value}{rename}");
        }

        if (status.Entries.Count > 20)
        {
            Console.WriteLine($"  … ve {status.Entries.Count - 20} girdi daha");
        }
    }

    private static async Task PrintRecentCommitsAsync(
        IGitProcessRunner runner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("son commit'ler:");

        CommitLogReader reader = new(runner);

        try
        {
            IReadOnlyList<CommitInfo> commits = await reader
                .ReadAsync(workingDirectory, new CommitLogQuery { MaxCount = 10 }, cancellationToken)
                .ConfigureAwait(false);

            foreach (CommitInfo commit in commits)
            {
                string refs = commit.Refs.Count > 0
                    ? $"  ({string.Join(", ", commit.Refs)})"
                    : string.Empty;

                Console.WriteLine(
                    $"  {commit.Id.ToShortString()} "
                    + $"{commit.Author.When.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} "
                    + $"{Truncate(commit.Author.Name, 18),-18} {commit.Subject}{refs}");
            }
        }
        catch (GitException ex) when (ex.Kind is GitFailureKind.UnknownRevision or GitFailureKind.Unknown)
        {
            // An unborn repository has no commits; this is not an error.
            Console.WriteLine("  (henüz commit yok)");
        }
    }

    private static void PrintCommandLog(InMemoryGitCommandLog log)
    {
        Console.WriteLine();
        Console.WriteLine($"çalıştırılan git komutları ({log.Entries.Count}):");

        foreach (GitCommandLogEntry entry in log.Entries)
        {
            Console.WriteLine(
                $"  {entry.Duration.TotalMilliseconds,6:F0} ms  "
                + $"{(entry.IsSuccess ? " " : "✗")} {entry.CommandLine}");
        }

        double total = log.Entries.Sum(e => e.Duration.TotalMilliseconds);
        Console.WriteLine($"  {total,6:F0} ms  toplam");
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";
}
