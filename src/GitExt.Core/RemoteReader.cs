using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Reads configured remote repositories (P06-T05).
/// </summary>
public interface IRemoteReader
{
    /// <summary>
    /// Reads all remotes in the repository, in name order.
    /// </summary>
    Task<IReadOnlyList<GitRemote>> ReadAllAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single remote; <see langword="null"/> if it doesn't exist.
    /// </summary>
    Task<GitRemote?> FindAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRemoteReader"/>
/// <remarks>
/// <para>
/// 🔴 <b>MEASURED — <c>git remote -v</c> is not reliably PARSEABLE</b> and so it's never used
/// (it was the channel the plan proposed). It breaks in three separate ways:
/// </para>
/// <list type="number">
///   <item><description>
///     The separator is a <b>tab</b>, but the <b>URL can also contain a tab</b>: putting a
///     tabbed URL in the config produces the line <c>tabbed⇥https://a⇥b/c.git (fetch)</c>.
///   </description></item>
///   <item><description>
///     The line count per name isn't fixed: after <c>set-url --add</c>, a single remote
///     produces <b>three lines</b> (1 fetch + 2 push).
///   </description></item>
///   <item><description>
///     The <c>(fetch)</c>/<c>(push)</c> suffix could be part of the URL; the URL isn't quoted.
///   </description></item>
/// </list>
/// <para>
/// The channel used is <b>two calls</b>:
/// </para>
/// <list type="number">
///   <item><description>
///     <c>git remote</c> → the <b>authoritative name list</b>. Safe to split line by line:
///     a remote name <b>cannot contain</b> a newline (measured — <c>git config</c> rejects
///     such a key with <c>invalid key (newline)</c>). ⚠️ There is <b>no</b> <c>git remote -z</c>.
///   </description></item>
///   <item><description>
///     <c>git config -z --get-regexp</c> → url/pushurl/fetch/tagopt, <b>raw</b> values, in one
///     call. 🔴 <c>-z</c> is required: the form without <c>-z</c> is <b>line-based</b>, and in
///     measurement a URL containing a newline was <b>split across two lines</b> in the output
///     — the parser would think the second part was a separate record.
///   </description></item>
/// </list>
/// <para>
/// 🔴 <b>Why is <c>git remote get-url</c> not used?</b> Two reasons, both measured: if
/// <c>url.&lt;base&gt;.insteadOf</c> is defined it returns the <b>rewritten</b> URL (differs
/// from the raw config), and for a remote without a URL it prints <b>the name itself</b> as
/// the URL.
/// </para>
/// </remarks>
public sealed class RemoteReader : IRemoteReader
{
    private readonly IGitProcessRunner _runner;

    public RemoteReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<GitRemote>> ReadAllAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult names = await _runner.RunCheckedAsync(
            GitCommand.Create(workingDirectory, "remote"),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> remoteNames =
        [
            .. names.GetStandardOutputText()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.TrimEnd('\r')),
        ];

        GitResult config = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", "-z", "--get-regexp", RemoteConfigParser.KeyPattern],

                // If there are no remotes at all, the exit code is 1 and output is empty;
                // this is not an error, it's the "none" answer.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return RemoteConfigParser.Parse(
            config.ExitCode == 0 ? config.SplitStandardOutputAtNul() : [],
            remoteNames);
    }

    public async Task<GitRemote?> FindAsync(
        string workingDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        IReadOnlyList<GitRemote> remotes =
            await ReadAllAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        // Ordinal: names are case SENSITIVE — measured, `Buyuk` and `buyuk` can exist at the
        // same time in the same repository.
        return remotes.FirstOrDefault(remote =>
            string.Equals(remote.Name, name, StringComparison.Ordinal));
    }
}

/// <summary>
/// Converts the output of <c>git config -z --get-regexp '^remote\.'</c> into <see cref="GitRemote"/>s.
/// </summary>
/// <remarks>
/// <para>
/// A separate class because there are <b>two callers</b>: <see cref="RemoteReader"/> and
/// <see cref="RefReader"/>. The lesson from P06-T04: answering the same question via two
/// separate paths lets one of them silently drift wrong — hence parsing lives in one place.
/// </para>
/// <para>
/// <b>Record format (measured):</b> each record is <c>key\nvalue</c>, records separated by
/// <c>NUL</c>. The value can contain a newline; the key cannot.
/// </para>
/// </remarks>
internal static class RemoteConfigParser
{
    /// <summary>Pattern of config keys to read.</summary>
    internal const string KeyPattern = "^remote\\.";

    private const string Prefix = "remote.";

    /// <param name="records"><c>key\nvalue</c> records split by <c>-z</c>.</param>
    /// <param name="knownNames">
    /// Authoritative name list from <c>git remote</c>. If given, name splitting uses it (a
    /// remote with <b>no</b> URL at all still needing to stay in the list requires this);
    /// if <see langword="null"/>, names are derived purely from the keys.
    /// </param>
    internal static IReadOnlyList<GitRemote> Parse(
        IReadOnlyList<string> records,
        IReadOnlyList<string>? knownNames)
    {
        Dictionary<string, Builder> builders = [];
        List<string> order = [];

        if (knownNames is not null)
        {
            foreach (string name in knownNames)
            {
                if (!builders.ContainsKey(name))
                {
                    builders[name] = new Builder();
                    order.Add(name);
                }
            }
        }

        foreach (string record in records)
        {
            int newline = record.IndexOf('\n', StringComparison.Ordinal);
            if (newline < 0)
            {
                // A key with no value (`git config --add remote.x.y` with an empty value) —
                // none of the keys we care about can be like this, so skip it.
                continue;
            }

            string key = record[..newline];
            string value = record[(newline + 1)..];

            if (!key.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (SplitKey(key, knownNames) is not { } parts)
            {
                continue;
            }

            (string name, string subKey) = parts;

            if (!builders.TryGetValue(name, out Builder? builder))
            {
                builder = new Builder();
                builders[name] = builder;
                order.Add(name);
            }

            switch (subKey)
            {
                case "url":
                    builder.FetchUrls.Add(value);
                    break;
                case "pushurl":
                    builder.PushUrls.Add(value);
                    break;
                case "fetch":
                    builder.FetchRefspecs.Add(value);
                    break;
                case "tagopt":
                    builder.TagOption = value;
                    break;
                default:
                    // `prune`, `proxy`, unknown user-defined sub-keys…
                    break;
            }
        }

        return
        [
            .. order
                .Select(name => builders[name].Build(name))
                .OrderBy(remote => remote.Name, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Splits a <c>remote.&lt;name&gt;.&lt;subkey&gt;</c> key into two.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED — the name can contain a DOT:</b> <c>git remote add a.b …</c> is valid
    /// and the key becomes <c>remote.a.b.url</c>. Reading with <c>Split('.')[1]</c> would
    /// think the name was <c>a</c>. The correct rule: the <b>last</b> dot separates the
    /// sub-key. If an authoritative name list is available, it's checked first (longest
    /// match), because unknown sub-keys can also contain dots.
    /// </remarks>
    private static (string Name, string SubKey)? SplitKey(
        string key,
        IReadOnlyList<string>? knownNames)
    {
        string remainder = key[Prefix.Length..];

        if (knownNames is not null)
        {
            string? best = null;

            foreach (string name in knownNames)
            {
                if (remainder.Length > name.Length
                    && remainder[name.Length] == '.'
                    && remainder.StartsWith(name, StringComparison.Ordinal)
                    && (best is null || name.Length > best.Length))
                {
                    best = name;
                }
            }

            if (best is not null)
            {
                return (best, remainder[(best.Length + 1)..]);
            }
        }

        int lastDot = remainder.LastIndexOf('.');

        return lastDot <= 0
            ? null
            : (remainder[..lastDot], remainder[(lastDot + 1)..]);
    }

    private sealed class Builder
    {
        public List<string> FetchUrls { get; } = [];

        public List<string> PushUrls { get; } = [];

        public List<string> FetchRefspecs { get; } = [];

        public string? TagOption { get; set; }

        public GitRemote Build(string name) => new()
        {
            Name = name,
            FetchUrls = FetchUrls,
            PushUrls = PushUrls,
            FetchRefspecs = FetchRefspecs,
            TagOption = TagOption,
        };
    }
}
