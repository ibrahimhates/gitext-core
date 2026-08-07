using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>Alt modülün durumu (P07-T19).</summary>
public enum SubmoduleStatusKind
{
    /// <summary>Kayıtlı ama içi boş — <c>init</c>/<c>update</c> gerekiyor.</summary>
    NotInitialized,

    /// <summary>Kayıtlı commit ile eşleşiyor.</summary>
    UpToDate,

    /// <summary>Üst deponun beklediğinden farklı bir commit'te.</summary>
    Modified,

    /// <summary>Birleştirme çakışması var.</summary>
    Conflicted,
}

/// <summary>Bir alt modül (P07-T19).</summary>
public sealed record Submodule
{
    public required RepositoryPath Path { get; init; }

    /// <summary>Alt modülün bulunduğu commit.</summary>
    public required string ObjectId { get; init; }

    public required SubmoduleStatusKind Status { get; init; }

    /// <summary>git'in parantez içinde verdiği açıklama (etiket/açıklama).</summary>
    public string Describe { get; init; } = string.Empty;

    /// <summary>Alt modüle "girip" ayrı depo gibi incelemek için mutlak yol.</summary>
    public string ResolvePath(string repositoryRoot) => Path.ToAbsolutePath(repositoryRoot);
}

/// <summary>Alt modül işlemleri (P07-T19).</summary>
public interface ISubmoduleReader
{
    Task<IReadOnlyList<Submodule>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary><c>git submodule update --init</c>.</summary>
    Task InitializeAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        bool recursive = false,
        CancellationToken cancellationToken = default);

    /// <summary><c>git submodule sync</c>: URL değişikliklerini yayar.</summary>
    Task SyncAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git submodule status</c> okuyucusu (P07-T19).
/// </summary>
/// <remarks>
/// Çıktı satırları <c>&lt;işaret&gt;&lt;sha&gt; &lt;yol&gt; (&lt;describe&gt;)</c>
/// biçiminde. Baştaki işaret durumu veriyor: <c>-</c> başlatılmamış, <c>+</c> farklı
/// commit'te, <c>U</c> çakışma, <b>boşluk</b> güncel.
/// </remarks>
public sealed class SubmoduleReader : ISubmoduleReader
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public SubmoduleReader(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<IReadOnlyList<Submodule>> ListAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "submodule", "status"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    internal static IReadOnlyList<Submodule> Parse(string output)
    {
        List<Submodule> modules = [];

        foreach (string raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.TrimEnd('\r');

            if (line.Length < 2)
            {
                continue;
            }

            SubmoduleStatusKind status = line[0] switch
            {
                '-' => SubmoduleStatusKind.NotInitialized,
                '+' => SubmoduleStatusKind.Modified,
                'U' => SubmoduleStatusKind.Conflicted,
                _ => SubmoduleStatusKind.UpToDate,
            };

            // Güncel durumda satır boşlukla başlıyor; diğerlerinde işaretle.
            string body = line[0] is '-' or '+' or 'U' ? line[1..] : line.TrimStart();

            int space = body.IndexOf(' ', StringComparison.Ordinal);

            if (space <= 0)
            {
                continue;
            }

            string objectId = body[..space];
            string rest = body[(space + 1)..];

            // Yol boşluk içerebilir; describe kısmı SONDAKİ parantez içinde.
            string describe = string.Empty;
            int open = rest.LastIndexOf(" (", StringComparison.Ordinal);

            if (open > 0 && rest.EndsWith(')'))
            {
                describe = rest[(open + 2)..^1];
                rest = rest[..open];
            }

            if (RepositoryPath.TryParse(rest, out RepositoryPath path))
            {
                modules.Add(new Submodule
                {
                    Path = path,
                    ObjectId = objectId,
                    Status = status,
                    Describe = describe,
                });
            }
        }

        return modules;
    }

    public Task InitializeAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["submodule", "update", "--init"];

        if (recursive)
        {
            arguments.Add("--recursive");
        }

        if (path is { } target && !target.IsEmpty)
        {
            arguments.Add("--");
            arguments.Add(target.Value);
        }

        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }

    public Task SyncAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["submodule", "sync"];

        if (path is { } target && !target.IsEmpty)
        {
            arguments.Add("--");
            arguments.Add(target.Value);
        }

        return _writer.RunAsync(workingDirectory, arguments, cancellationToken);
    }
}
