using System.Text;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Produces a <c>.gitignore</c> line (P05-T08).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>MEASURED:</b> writing the file name <b>raw</b> silently fails. A name starting with
/// <c>#</c> is a comment, one starting with <c>!</c> is a negation, one containing <c>[</c> is a
/// character class, one containing <c>\</c> is an escape — in all four git does <b>not</b>
/// ignore the file, and it does not report an error either. The user says "ignore", the app
/// says "done", and the file stays in the list.
/// </para>
/// <para>
/// The produced pattern is <b>anchored to the root</b> (leading <c>/</c>): unanchored it would
/// match <b>every file with the same name</b> in the repository — while the user picked one.
/// </para>
/// </remarks>
public static class GitIgnorePattern
{
    /// <summary>
    /// Turns the given path into a pattern that ignores <b>only that path</b>.
    /// </summary>
    public static string ForPath(RepositoryPath path) => "/" + Escape(path.Value);

    /// <summary>
    /// Produces a pattern ignoring the directory the given path lives in; <see langword="null"/>
    /// if the path belongs to the root.
    /// </summary>
    public static string? ForDirectoryOf(RepositoryPath path)
    {
        int separator = path.Value.LastIndexOf('/');

        return separator <= 0 ? null : "/" + Escape(path.Value[..separator]) + "/";
    }

    /// <summary>
    /// Produces a pattern ignoring <b>all</b> files with the same extension;
    /// <see langword="null"/> if there is no extension.
    /// </summary>
    /// <remarks>
    /// This pattern is deliberately <b>not anchored</b>: the request "all <c>.log</c> files" must
    /// by definition apply in every directory.
    /// </remarks>
    public static string? ForExtensionOf(RepositoryPath path)
    {
        string name = path.Value[(path.Value.LastIndexOf('/') + 1)..];

        // A leading dot is not an extension but a hidden file name (`.env` → no extension).
        int dot = name.LastIndexOf('.');

        return dot <= 0 || dot == name.Length - 1 ? null : "*" + Escape(name[dot..]);
    }

    /// <summary>
    /// Makes a path match <b>literally</b> inside a <c>.gitignore</c> pattern.
    /// </summary>
    /// <remarks>
    /// What is escaped and why (all measured): <c>\</c> the escape character · <c>*</c> and
    /// <c>?</c> wildcards · <c>[</c> start of a character class · <c>#</c> at line start, a comment ·
    /// <c>!</c> at line start, a negation.
    /// <para>
    /// Spaces are not escaped: measured, an <b>inline</b> space is no problem. Only a space at the
    /// <b>end</b> of the line is trimmed by git; in that case the last character is escaped.
    /// </para>
    /// </remarks>
    public static string Escape(string value)
    {
        StringBuilder builder = new(value.Length + 4);

        foreach (char c in value)
        {
            if (c is '\\' or '*' or '?' or '[')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        // `#` and `!` are special only at the START of a line; when the pattern begins with `/` it is
        // already fine, but this is needed for unanchored patterns (extension).
        if (builder.Length > 0 && builder[0] is '#' or '!')
        {
            builder.Insert(0, '\\');
        }

        // A trailing space is trimmed by git; unescaped, the pattern loses the last character of the
        // name and matches nothing.
        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Insert(builder.Length - 1, '\\');
        }

        return builder.ToString();
    }
}
