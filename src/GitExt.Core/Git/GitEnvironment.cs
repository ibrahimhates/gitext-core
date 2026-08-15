using System.Diagnostics;

namespace GitExt.Core.Git;

/// <summary>
/// Environment and configuration settings that make every <c>git</c> call predictable (ADR-0002).
/// </summary>
/// <remarks>
/// Without these settings <c>git</c>'s behaviour changes with the user's locale, terminal and
/// global configuration; the parsers then break silently along with it.
/// </remarks>
internal static class GitEnvironment
{
    /// <summary>
    /// Prepares the process environment.
    /// </summary>
    internal static void Apply(ProcessStartInfo startInfo, bool isReadOnly)
    {
        // Locale-independent, English and deterministic output.
        // Otherwise date formats and error texts change with the user's language.
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";

        // CRITICAL: without this, a command that asks for authentication waits for a terminal and
        // locks the application indefinitely. Instead it fails and we handle that.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        // Keep commands that try to open an editor (commit, rebase -i, tag -a) from hanging.
        // Commands that need these variables set them deliberately themselves.
        startInfo.Environment["GIT_EDITOR"] = "false";
        startInfo.Environment["GIT_SEQUENCE_EDITOR"] = "false";

        // A pager is meaningless in a child process and can corrupt the output.
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";

        // Prevent graphical authentication tools from popping up — we drive the UI ourselves.
        startInfo.Environment["GIT_ASKPASS"] = string.Empty;
        startInfo.Environment["SSH_ASKPASS"] = string.Empty;

        // Read-only calls must not try to write the index; otherwise they collide with a concurrent
        // write operation over index.lock.
        if (isReadOnly)
        {
            startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        }
    }

    /// <summary>
    /// The <c>-c</c> configuration overrides prepended to every command.
    /// </summary>
    /// <remarks>
    /// Because they are passed as arguments they do not permanently change the user's
    /// <c>.gitconfig</c>; they apply only to that one call.
    /// </remarks>
    internal static IEnumerable<string> ConfigurationOverrides()
    {
        // Emit non-ASCII file names as they are, not as octal escapes like \303\266.
        // So the parsers never have to decode those escapes.
        yield return "-c";
        yield return "core.quotepath=false";

        // Advice texts write long blocks to stderr. We will show these messages to the user in our
        // own UI; their raw form drowns stderr in noise.
        yield return "-c";
        yield return "advice.detachedHead=false";

        // Always take commit messages as UTF-8.
        // git converts from the encoding stored in the object (the encoding line) to this setting;
        // measured: a message stored as ISO-8859-9 is 0xFC in the raw object, 0xC3 0xBC in log output.
        // The default is already UTF-8 but the user can change it in .gitconfig — in that case our
        // parsers would break silently. We force it explicitly.
        yield return "-c";
        yield return "i18n.logOutputEncoding=UTF-8";
    }
}
