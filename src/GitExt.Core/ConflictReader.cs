using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// One of the three versions of a conflicting file in the index (P07-T02).
/// </summary>
/// <remarks>
/// The numbers are git's own stage numbers; they are used exactly this way in notations such as
/// <c>git show :2:&lt;path&gt;</c>.
/// </remarks>
public enum ConflictStage
{
    /// <summary>Ortak ata (<c>:1:</c>).</summary>
    Base = 1,

    /// <summary>Our version — <c>HEAD</c> (<c>:2:</c>).</summary>
    Ours = 2,

    /// <summary>The other side's version (<c>:3:</c>).</summary>
    Theirs = 3,
}

/// <summary>
/// A single conflicting file (P07-T01).
/// </summary>
public sealed record ConflictedFile
{
    public required RepositoryPath Path { get; init; }

    public required ConflictKind Kind { get; init; }

    /// <summary>Is the common ancestor version in the index?</summary>
    public bool HasBase { get; init; }

    /// <summary>Is our version in the index?</summary>
    public bool HasOurs { get; init; }

    /// <summary>Is the other side's version in the index?</summary>
    public bool HasTheirs { get; init; }

    /// <summary>Is it a submodule conflict?</summary>
    public bool IsSubmodule { get; init; }

    /// <summary>Is the given stage in the index?</summary>
    /// <remarks>
    /// 🔴 Running <c>git show :2:&lt;path&gt;</c> without asking this gives a <b>fatal</b>
    /// (measured: <c>is in the index, but not at stage 2</c>). Swallowing the error and returning
    /// empty text would read as "the file was empty" — a deleted file and an empty file are very
    /// different things to the user.
    /// </remarks>
    public bool HasStage(ConflictStage stage) => stage switch
    {
        ConflictStage.Base => HasBase,
        ConflictStage.Ours => HasOurs,
        ConflictStage.Theirs => HasTheirs,
        _ => false,
    };

    /// <summary>
    /// Is this conflict at the <b>content</b> level, or at the <b>presence</b> level?
    /// </summary>
    /// <remarks>
    /// In a presence conflict (one side deleted it) a three-way text view is meaningless: there are
    /// not two texts to merge, there is a decision to make — "delete" or "keep".
    /// </remarks>
    public bool IsContentConflict => HasOurs && HasTheirs;
}

/// <summary>Conflict reading (P07-T01, P07-T02).</summary>
public interface IConflictReader
{
    /// <summary>Lists the conflicting files.</summary>
    Task<IReadOnlyList<ConflictedFile>> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a conflicting file's content at the given stage.
    /// </summary>
    /// <returns><see langword="null"/> when the stage is not in the index.</returns>
    Task<byte[]?> ReadStageAsync(
        string workingDirectory,
        RepositoryPath path,
        ConflictStage stage,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the conflicts in the index (P07-T01, P07-T02).
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED — the layout of a <c>u</c> line:</b>
/// <c>u &lt;XY&gt; &lt;sub&gt; &lt;m1&gt; &lt;m2&gt; &lt;m3&gt; &lt;mW&gt; &lt;h1&gt; &lt;h2&gt; &lt;h3&gt; &lt;path&gt;</c>.
/// A missing stage's <b>mode</b> comes back as <c>000000</c> — which versions exist is known from
/// there, not by trial and error.
/// </para>
/// <para>
/// 🔴 <b><c>-z</c> is mandatory.</b> Measured: without <c>-z</c> git C-quotes the path
/// (<c>şğüıöç.txt</c> → <c>"\305\237\304\237…"</c>). Turkish paths would be silently corrupted.
/// </para>
/// <para>
/// Why is <see cref="StatusReader"/> not enough? It gives the <b>kind</b> of the conflict but drops
/// which stages exist; the three-way view rests on exactly that.
/// </para>
/// </remarks>
public sealed class ConflictReader : IConflictReader
{
    /// <summary>The mode of a stage that does not exist.</summary>
    private const string AbsentMode = "000000";

    private readonly IGitProcessRunner _runner;

    public ConflictReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<ConflictedFile>> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["status", "--porcelain=v2", "-z", "--untracked-files=no"],
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    public async Task<byte[]?> ReadStageAsync(
        string workingDirectory,
        RepositoryPath path,
        ConflictStage stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (path.IsEmpty)
        {
            return null;
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory,
                "show",
                $":{(int)stage}:{path.Value}"),
            cancellationToken).ConfigureAwait(false);

        // When the stage is absent git says `fatal: … but not at stage N`. That is not an error but a
        // STATE: "there is no file on this side". Telling null from an empty file is essential.
        //
        // Raw bytes are returned: the content may not be text, and even when it is, its encoding is
        // the repository's own. The decision to convert to text belongs to the layer above (P04's
        // encoding detection).
        return result.IsSuccess ? result.StandardOutput : null;
    }

    /// <summary>Parses the <c>u</c> records in the <c>--porcelain=v2 -z</c> output.</summary>
    internal static IReadOnlyList<ConflictedFile> Parse(string output)
    {
        List<ConflictedFile> files = [];

        foreach (string record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!record.StartsWith("u ", StringComparison.Ordinal))
            {
                continue;
            }

            // A path can contain spaces; the first 10 fields are fixed, the rest is the path.
            string[] parts = record.Split(' ', 11);

            if (parts.Length < 11 || !RepositoryPath.TryParse(parts[10], out RepositoryPath path))
            {
                continue;
            }

            files.Add(new ConflictedFile
            {
                Path = path,
                Kind = StatusReader.ParseConflict(parts[1]),
                IsSubmodule = parts[2].StartsWith('S'),
                HasBase = !string.Equals(parts[3], AbsentMode, StringComparison.Ordinal),
                HasOurs = !string.Equals(parts[4], AbsentMode, StringComparison.Ordinal),
                HasTheirs = !string.Equals(parts[5], AbsentMode, StringComparison.Ordinal),
            });
        }

        return files;
    }
}
