namespace GitExt.UI.Localization;

/// <summary>
/// An available user interface language (P11-T01).
/// </summary>
/// <param name="Code">Language code taken from the file name: <c>en</c>, <c>tr</c>.</param>
/// <param name="Name">
/// The language's name <b>in that language</b>: <c>English</c>, <c>Türkçe</c>.
/// </param>
/// <remarks>
/// The name is read from the <c>_meta.name</c> field of the language file, not from a table
/// baked into the code. The reason: adding a language should be <b>only</b> adding a JSON
/// file. If names lived in code, someone adding <c>fr.json</c> would see "fr" in the list and
/// would have to touch a C# file just to write the name.
/// <para>
/// Why the name is in its own language rather than English: showing "Turkish" in the language
/// picker means it is written in a language the person looking for it does not read. The list
/// exists to be read by someone searching for that language, not by someone who does not
/// speak it.
/// </para>
/// </remarks>
public sealed record LanguageInfo(string Code, string Name)
{
    /// <summary>Text shown in the dropdown.</summary>
    public override string ToString() => Name;
}
