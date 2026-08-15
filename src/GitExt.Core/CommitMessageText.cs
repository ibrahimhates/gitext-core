namespace GitExt.Core;

/// <summary>
/// Helpers that work on commit message text with git's own rules (P05-T13).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>The reason this class exists was measured.</b> Our commit path is
/// <c>git commit -F - --cleanup=whitespace</c> (P05-T06), and in that mode comment lines are
/// <b>kept</b> — a deliberate decision, so the user's issue references such as <c>#123</c> are
/// not lost. But git's <b>editor</b> path (<c>--cleanup=default</c>) <b>deletes</b> comment
/// lines, and <c>commit.template</c> together with <c>.git/MERGE_MSG</c> is exactly the input
/// of that path:
/// </para>
/// <code>
/// MERGE_MSG:                          the commit git produces via the editor:
///   Merge branch 'dev'                  Merge branch 'dev'
///                                    ←  (NO comments)
///   # Conflicts:
///   #	a.txt
/// </code>
/// <para>
/// So if we loaded these files into the box as they are and committed, the user would get a
/// message they would <b>not</b> get doing it with git itself — <c># Conflicts:</c> lines in
/// the commit body. Comments are cleaned <b>while loading</b> (what is in the box = what gets
/// committed); the text the user typed themselves is never touched.
/// </para>
/// </remarks>
public static class CommitMessageText
{
    /// <summary>git's default when <c>core.commentChar</c> is not set.</summary>
    public const string DefaultCommentCharacter = "#";

    /// <summary>
    /// Turns the comment character setting into a concrete value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MEASURED:</b> <c>core.commentChar</c> does not have to be <c>#</c>. Set to <c>;</c>, git
    /// deletes lines starting with <c>;</c> and <b>keeps</b> those starting with <c>#</c> — a blind
    /// <c>#</c> filter would in that repository both leave the real comments in and delete the
    /// user's issue lines. The value can also be <b>multi-character</b>
    /// (git 2.45+; <c>//</c> was accepted).
    /// </para>
    /// <para>
    /// <c>auto</c> is a special value (<i>deprecated</i> in git 2.55, gone in git 3.0): git picks
    /// a character unused in the message, so it has no fixed answer. In that case we fall back to
    /// the default — leaving the comment in is better than deleting the user's line on a wrong
    /// guess.
    /// </para>
    /// </remarks>
    public static string ResolveCommentCharacter(string? configuredValue) =>
        configuredValue switch
        {
            null or "" => DefaultCommentCharacter,
            "auto" => DefaultCommentCharacter,
            _ => configuredValue,
        };

    /// <summary>
    /// Deletes comment lines — exactly what git's <c>--cleanup=default</c> path does.
    /// </summary>
    /// <param name="text">Template or <c>MERGE_MSG</c> content.</param>
    /// <param name="commentCharacter">Comment prefix; the default is used when empty.</param>
    /// <remarks>
    /// <b>MEASURED:</b> only a prefix at the <b>start of a line</b> counts as a comment — git
    /// itself does <b>not</b> delete a <c>␣␣# indented</c> line either. Adding a <c>TrimStart</c>
    /// would mean deleting real text in a template that contains a code snippet.
    /// </remarks>
    public static string RemoveComments(string text, string? commentCharacter = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        string prefix = ResolveCommentCharacter(commentCharacter);

        if (text.Length == 0)
        {
            return text;
        }

        // The line ending style is preserved: if the file is CRLF it stays CRLF. As with patches
        // (P04-T07), normalising line endings is not our job here either.
        string[] lines = text.Split('\n');

        IEnumerable<string> kept = lines.Where(line =>
            !line.StartsWith(prefix, StringComparison.Ordinal));

        return string.Join('\n', kept);
    }

    /// <summary>
    /// Prepares the text to load into the box: comments removed, leading/trailing blank lines dropped.
    /// </summary>
    /// <remarks>
    /// Once the comments are gone a lot of blank lines usually remain (in <c>MERGE_MSG</c>, two
    /// blank lines after the subject line, plus the end of file). Having the caret start in the
    /// <b>middle</b> of the text would give the user the feeling that "something was here".
    /// </remarks>
    public static string PrepareForEditing(string text, string? commentCharacter = null) =>
        RemoveComments(text, commentCharacter).Trim('\n', '\r', ' ', '\t');
}
