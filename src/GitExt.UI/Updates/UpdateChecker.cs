using System.Net.Http.Headers;
using System.Text.Json;

namespace GitExt.UI.Updates;

/// <summary>
/// A release published on the project's release page (P13-T01).
/// </summary>
/// <param name="Version">The version, as the tag names it (<c>v0.1.1</c>).</param>
/// <param name="Url">The page a person can read about it on.</param>
public sealed record ReleaseNote(string Version, string Url);

/// <summary>
/// Finds out what the latest published release is.
/// </summary>
public interface IReleaseFeed
{
    /// <summary>
    /// The latest release, or <see langword="null"/> when it could not be established.
    /// </summary>
    /// <remarks>
    /// <b>Never throws.</b> Being unable to reach the network is the normal state of an offline
    /// machine, not an error the user has to be told about.
    /// </remarks>
    Task<ReleaseNote?> GetLatestAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the latest release from GitHub (P13-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED:</b> <c>api.github.com/repos/…/releases/latest</c> answers in ~0.44 s and gives the
/// tag in <c>tag_name</c> and the page in <c>html_url</c>. Nothing else is read, nothing is sent
/// beyond the request itself, and there is no authentication: the endpoint is public.
/// </para>
/// <para>
/// ⚠️ GitHub <b>requires</b> a <c>User-Agent</c>; without one the API answers 403. That is the one
/// thing about this request that is not obvious.
/// </para>
/// </remarks>
public sealed class GitHubReleaseFeed : IReleaseFeed, IDisposable
{
    /// <summary>The public endpoint for the newest release.</summary>
    public const string LatestReleaseUrl =
        "https://api.github.com/repos/ibrahimhates/gitext-core/releases/latest";

    private readonly HttpClient _client;
    private readonly string _url;
    private readonly bool _ownsClient;

    public GitHubReleaseFeed(HttpClient? client = null, string? url = null)
    {
        _ownsClient = client is null;

        _client = client ?? new HttpClient
        {
            // A version check must never be something the user waits for.
            Timeout = TimeSpan.FromSeconds(10),
        };

        _url = url ?? LatestReleaseUrl;

        if (!_client.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("gitext-core", "1.0"));
        }

        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseNote?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _client
                .GetAsync(_url, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 404 is a legitimate answer: a repository with no releases yet.
                return null;
            }

            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return Read(document.RootElement);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or OperationCanceledException
                or JsonException
                or IOException)
        {
            // Offline, blocked, rate-limited, or the answer was not what we expected: no news.
            return null;
        }
    }

    /// <summary>
    /// Picks the two fields that matter out of the answer.
    /// </summary>
    /// <remarks>
    /// Read by hand with <see cref="JsonDocument"/> rather than deserialised: the answer has ~40
    /// fields we do not care about, and a typed read is also the thing that breaks under trimming
    /// (the trap measured in P03-T16).
    /// </remarks>
    private static ReleaseNote? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // A draft is not published and a pre-release is not what someone on a release build wants
        // to be nudged towards.
        if (IsTrue(root, "draft") || IsTrue(root, "prerelease"))
        {
            return null;
        }

        if (!root.TryGetProperty("tag_name", out JsonElement tag)
            || tag.GetString() is not { Length: > 0 } version)
        {
            return null;
        }

        string url = root.TryGetProperty("html_url", out JsonElement page)
            && page.GetString() is { Length: > 0 } address
                ? address
                : "https://github.com/ibrahimhates/gitext-core/releases";

        return new ReleaseNote(version, url);

        static bool IsTrue(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.True;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
