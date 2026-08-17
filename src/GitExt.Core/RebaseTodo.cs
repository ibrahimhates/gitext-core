using System.Runtime.Versioning;
using System.Text;

namespace GitExt.Core;

/// <summary>
/// The action of one step in an interactive rebase todo list (P07-T10).
/// </summary>
/// <remarks>
/// The names are git's own verbs; they are written into the todo file exactly like this.
/// </remarks>
public enum RebaseAction
{
    /// <summary>Apply the commit as it is.</summary>
    Pick,

    /// <summary>Apply it but change the message.</summary>
    Reword,

    /// <summary>Apply it and stop for editing.</summary>
    Edit,

    /// <summary>Fold into the previous one, combining the messages.</summary>
    Squash,

    /// <summary>Fold into the previous one, discarding <b>this</b> commit's message.</summary>
    Fixup,

    /// <summary>Drop the commit entirely.</summary>
    Drop,
}

/// <summary>
/// A single line in an interactive rebase todo list (P07-T10).
/// </summary>
public sealed record RebaseStep
{
    /// <summary>The commit's full SHA.</summary>
    public required string ObjectId { get; init; }

    /// <summary>The commit subject — for display only.</summary>
    public string Subject { get; init; } = string.Empty;

    public RebaseAction Action { get; init; } = RebaseAction.Pick;

    /// <summary>
    /// The new message the user typed, for <see cref="RebaseAction.Reword"/>.
    /// </summary>
    public string? NewMessage { get; init; }

    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;
}

/// <summary>
/// An interactive rebase todo list (P07-T10).
/// </summary>
/// <remarks>
/// <para>
/// git normally opens this list in an editor. We point <c>GIT_SEQUENCE_EDITOR</c> at our own
/// script and write the list <b>programmatically</b>.
/// </para>
/// <para>
/// 🔴 <b>MEASURED — the file handed to the script arrives ALREADY FULL of git's own todo.</b>
/// In the first measurement the script appended with <c>&gt;&gt;</c>, so git saw <b>6</b> commands
/// instead of 3, the commits were applied twice and conflicted. The writer has to <b>truncate</b>
/// the file — <see cref="RebaseTodoSession"/>'s script does exactly that, and a test pins it down.
/// </para>
/// </remarks>
public static class RebaseTodo
{
    /// <summary>Produces the contents of the todo file.</summary>
    /// <remarks>
    /// MEASURED: <c>pick &lt;sha&gt;</c> is enough — git ignores the rest of the line, writing the
    /// subject is not required. It is written anyway: if something goes wrong, the <b>person</b>
    /// looking at <c>.git/rebase-merge/git-rebase-todo</c> must be able to see what happened. Both
    /// the short and the full SHA are accepted; the full SHA is written so an abbreviation clash
    /// can never arise.
    /// </remarks>
    public static string Render(IReadOnlyList<RebaseStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        StringBuilder builder = new();

        foreach (RebaseStep step in steps)
        {
            if (step.Action == RebaseAction.Drop)
            {
                // Writing `drop` and not writing the line at all give the same result; `drop` is
                // written because the intent must be clear to anyone looking at the file.
                builder.Append("drop ");
            }
            else
            {
                builder.Append(Verb(step.Action)).Append(' ');
            }

            builder.Append(step.ObjectId);

            if (step.Subject is { Length: > 0 } subject)
            {
                // A line ending would break the todo; the subject is reduced to a single line.
                builder.Append(" # ").Append(subject.ReplaceLineEndings(" "));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    internal static string Verb(RebaseAction action) => action switch
    {
        RebaseAction.Reword => "reword",
        RebaseAction.Edit => "edit",
        RebaseAction.Squash => "squash",
        RebaseAction.Fixup => "fixup",
        RebaseAction.Drop => "drop",
        _ => "pick",
    };

    /// <summary>
    /// Will git accept this todo list?
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>MEASURED — an empty todo gives <c>error: nothing to do</c> with rc=1</b> and the
    /// rebase never starts (the repository is left untouched — safe, but the user is puzzled that
    /// "nothing happened"). Making every step a <c>drop</c> comes out at the same place.
    /// </para>
    /// <para>
    /// ⚠️ The first step cannot be <c>squash</c> or <c>fixup</c>: there is no previous commit to
    /// fold into. git says <c>cannot 'squash' without a previous commit</c> in that case.
    /// </para>
    /// </remarks>
    public static string? Validate(IReadOnlyList<RebaseStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        List<RebaseStep> kept = [.. steps.Where(step => step.Action != RebaseAction.Drop)];

        if (kept.Count == 0)
        {
            return "Every commit was removed — nothing is left to apply.";
        }

        if (kept[0].Action is RebaseAction.Squash or RebaseAction.Fixup)
        {
            return "The first commit cannot be squashed into a previous one; there is no previous commit.";
        }

        return null;
    }
}

/// <summary>
/// A temporary session that sets up <c>GIT_SEQUENCE_EDITOR</c> (and <c>GIT_EDITOR</c> when needed)
/// (P07-T10).
/// </summary>
/// <remarks>
/// <para>
/// The pattern comes from <see cref="AskPassSession"/>: git is handed a <b>script path</b> and the
/// actual content is not embedded in the script — the script copies it from a file read out of the
/// <b>environment</b>. That way the todo text never appears on the command line or in the script
/// body, and the quotes and newlines inside it raise no escaping problems.
/// </para>
/// <para>
/// MEASURED — if the sequence editor <b>fails</b>, git never starts the rebase (rc=1, no
/// <c>rebase-merge</c> directory, the repository untouched). So a failure of the script does not
/// lead to a half-finished state.
/// </para>
/// </remarks>
public sealed class RebaseTodoSession : IDisposable
{
    /// <summary>Path of the file the todo content is read from.</summary>
    internal const string TodoVariable = "GITEXT_REBASE_TODO";

    /// <summary>Path of the file the new commit message is read from.</summary>
    internal const string MessageVariable = "GITEXT_REBASE_MESSAGE";

    private readonly List<string> _paths = [];
    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);
    private bool _disposed;

    private RebaseTodoSession()
    {
    }

    /// <summary>Environment variables to add to the command.</summary>
    public IReadOnlyDictionary<string, string> Environment => _environment;

    /// <summary>
    /// Sets up a session that writes the todo list (and optionally a new message).
    /// </summary>
    /// <param name="todo">The contents of the todo file.</param>
    /// <param name="message">
    /// The message to use for <c>reword</c>/<c>squash</c>; when <see langword="null"/>, the message
    /// git prepared is accepted unchanged.
    /// </param>
    public static RebaseTodoSession Create(string todo, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(todo);

        RebaseTodoSession session = new();

        session._environment["GIT_SEQUENCE_EDITOR"] = session.WriteScript("seq", TodoVariable);
        session._environment[TodoVariable] = session.WriteTemporary("todo", todo);

        if (message is not null)
        {
            session._environment["GIT_EDITOR"] = session.WriteScript("msg-editor", MessageVariable);
            session._environment[MessageVariable] = session.WriteTemporary("msg", message);
        }
        else
        {
            // When no message is given the editor must not open at all; `true` always succeeds
            // silently and git reads that as "the user did not change it". On Windows as well:
            // git runs the editor through the bundled MSYS `sh`, where `true` is a builtin
            // (measured — see ConflictResolver.NonInteractiveEditor).
            session._environment["GIT_EDITOR"] = "true";
        }

        return session;
    }

    private string WriteTemporary(string kind, string content)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"gitext-rebase-{kind}-{Guid.NewGuid():N}");

        File.WriteAllText(path, content.ReplaceLineEndings("\n"), new UTF8Encoding(false));
        _paths.Add(path);
        return path;
    }

    /// <summary>
    /// The script that writes the file named by the given environment variable <b>over</b> the
    /// target git supplies.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>&gt;</c> (truncate and write) is used, not <c>&gt;&gt;</c>. In the measurement,
    /// appending put ours after git's own todo and caused the commits to be applied twice.
    /// </remarks>
    private string WriteScript(string kind, string variable)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"gitext-rebase-{kind}-{Guid.NewGuid():N}{(OperatingSystem.IsWindows() ? ".cmd" : ".sh")}");

        string script = OperatingSystem.IsWindows()
            ? $"@echo off\r\ntype \"%{variable}%\" > %1\r\n"
            : $"#!/bin/sh\ncat \"${variable}\" > \"$1\"\n";

        File.WriteAllText(path, script, new UTF8Encoding(false));

        if (!OperatingSystem.IsWindows())
        {
            MakeExecutable(path);
        }

        _paths.Add(path);
        return ShellSafePath(path);
    }

    /// <summary>
    /// The form of the script path git can hand to <b>the shell</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 MEASURED (Git for Windows 2.55, under Wine) — git does not start the editor directly, it
    /// runs it through <c>sh -c</c>, and in MSYS <c>sh</c> a backslash is an <b>escape character</b>:
    /// <c>C:\temp\seq.cmd</c> arrives as <c>C:tempseq.cmd</c> (the <c>\t</c> even becomes a real tab)
    /// and git answers <c>error: there was a problem with the editor</c>. Every interactive rebase
    /// failed on Windows for this reason and the rebase never started.
    /// <para>
    /// Forward slashes take care of the escaping and the quotes take care of a temp path containing
    /// a space; both were measured (rc=0, the todo really applied). The path added to
    /// <see cref="_paths"/> stays the ORIGINAL one — deletion goes through the file system, not
    /// through a shell.
    /// </para>
    /// </remarks>
    private static string ShellSafePath(string path) =>
        OperatingSystem.IsWindows() ? $"\"{path.Replace('\\', '/')}\"" : path;

    [UnsupportedOSPlatform("windows")]
    private static void MakeExecutable(string path) => File.SetUnixFileMode(
        path,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (string path in _paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Failing to delete it does not break anything.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
