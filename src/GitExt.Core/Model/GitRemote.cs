namespace GitExt.Core.Model;

/// <summary>
/// A configured remote (P06-T05).
/// </summary>
/// <remarks>
/// <para>
/// The values are <b>raw config</b> values. 🔴 <b>MEASURED:</b> with
/// <c>url.&lt;base&gt;.insteadOf</c> defined, <c>git remote get-url</c> and <c>git remote -v</c> both
/// give the URL in its <b>rewritten</b> form: with <c>example:project</c> in the config, both say
/// <c>/…/up.gitproject</c>. If the UI puts that value into the edit box and saves it, the user's
/// shortcut is <b>permanently destroyed</b>. That is why the values here are read only from
/// <c>git config</c>.
/// </para>
/// <para>
/// The URL lists are <b>plural</b>: <c>git remote set-url --add</c> can write several URLs to the
/// same remote (fetch uses the first, push goes to all of them).
/// </para>
/// </remarks>
public sealed record GitRemote
{
    /// <summary>The remote's name (<c>origin</c> and the like).</summary>
    public required string Name { get; init; }

    /// <summary><c>remote.&lt;name&gt;.url</c> — raw, in order.</summary>
    public IReadOnlyList<string> FetchUrls { get; init; } = [];

    /// <summary>
    /// <c>remote.&lt;name&gt;.pushurl</c> — raw. When empty, push uses <see cref="FetchUrls"/>.
    /// </summary>
    public IReadOnlyList<string> PushUrls { get; init; } = [];

    /// <summary><c>remote.&lt;ad&gt;.fetch</c> refspec'leri.</summary>
    public IReadOnlyList<string> FetchRefspecs { get; init; } = [];

    /// <summary><c>remote.&lt;ad&gt;.tagopt</c> (<c>--tags</c> / <c>--no-tags</c>).</summary>
    public string? TagOption { get; init; }

    /// <summary>
    /// The primary URL to display; <b><see langword="null"/> when none is defined</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>MEASURED:</b> for a remote with only a <c>fetch</c> key defined,
    /// <c>git remote get-url &lt;name&gt;</c> prints <b>the name itself</b> with exit code <b>0</b>
    /// (git takes the name for a URL), while <c>remote -v</c> leaves it blank. Two different answers to
    /// the same question; neither is used.
    /// </remarks>
    public string? Url => FetchUrls.Count > 0 ? FetchUrls[0] : null;

    /// <summary>Is a separate URL defined for pushing?</summary>
    /// <remarks>
    /// The answer to this question is <b>not</b> in <c>remote -v</c>'s <c>(push)</c> line: with no
    /// pushurl defined, git repeats the fetch URL there.
    /// </remarks>
    public bool HasSeparatePushUrl => PushUrls.Count > 0;

    /// <summary>The URLs a push will actually go to.</summary>
    public IReadOnlyList<string> EffectivePushUrls =>
        PushUrls.Count > 0 ? PushUrls : FetchUrls;

    /// <summary>
    /// Is the <c>fetch</c> refspec the default one git sets up?
    /// </summary>
    /// <remarks>
    /// A non-default refspec is <b>not updated</b> by git on a rename (measured; the warning is on
    /// stderr alone, with exit code 0).
    /// </remarks>
    public bool HasDefaultFetchRefspec =>
        FetchRefspecs.Count == 1
        && string.Equals(FetchRefspecs[0], DefaultFetchRefspec(Name), StringComparison.Ordinal);

    /// <summary>The default fetch refspec git sets up for a remote.</summary>
    public static string DefaultFetchRefspec(string name) =>
        $"+refs/heads/*:refs/remotes/{name}/*";

    /// <summary>
    /// Hides the password in a URL: <c>https://ali:s3cr3t@host/x.git</c> →
    /// <c>https://ali:***@host/x.git</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ For <b>display</b> only. A masked value is never put into an edit box: the user saves
    /// <c>***</c> and breaks their own password.
    /// </remarks>
    public static string MaskCredentials(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return string.Empty;
        }

        // User information can only appear in the `scheme://` form; in the `git@host:path` (scp-like)
        // form the `:` comes before the path and carries no password.
        int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return url;
        }

        int authorityStart = schemeEnd + 3;
        int at = url.IndexOf('@', authorityStart);
        if (at < 0)
        {
            return url;
        }

        int colon = url.IndexOf(':', authorityStart);
        if (colon < 0 || colon > at)
        {
            // No password, only a user name.
            return url;
        }

        return string.Concat(url.AsSpan(0, colon + 1), "***", url.AsSpan(at));
    }
}
