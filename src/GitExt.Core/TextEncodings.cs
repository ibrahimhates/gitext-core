using System.Text;

namespace GitExt.Core;

/// <summary>
/// Resolves text encodings and makes legacy code pages usable (P04-T07).
/// </summary>
/// <remarks>
/// <para>
/// <b>MEASURED:</b> by default .NET keeps only UTF-8/16/32, ASCII and Latin-1 <i>registered</i>;
/// <c>Encoding.GetEncoding("ISO-8859-9")</c> <b>throws</b>. For a Git UI this is unacceptable:
/// files in the user's repository may be in Windows-1254, Shift-JIS or some other legacy code
/// page, and they become unreadable in the diff.
/// The <c>System.Text.Encoding.CodePages</c> package is <b>not needed</b> — the provider is
/// already in .NET 10's shared framework, it only has to be registered (trying to add the
/// NuGet package produced an NU1510 "unnecessary" warning).
/// </para>
/// <para>
/// Once <see cref="CodePagesEncodingProvider"/> is registered, <see cref="Encoding.GetEncoding(string)"/>
/// works everywhere. Registration happens in the static constructor, i.e. on first access to
/// this class. Since every encoding lookup goes through <see cref="TryGet"/> that is enough;
/// <c>ModuleInitializer</c> is not recommended in library code (CA2255).
/// </para>
/// </remarks>
public static class TextEncodings
{
    /// <summary>The default used when no encoding name is given.</summary>
    public static Encoding Default => Encoding.UTF8;

    static TextEncodings() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Makes sure the legacy code pages are registered.
    /// </summary>
    /// <remarks>
    /// Does nothing beyond triggering the static constructor. Called once at application startup
    /// so that <see cref="Encoding.GetEncoding(string)"/> works everywhere.
    /// </remarks>
    public static void EnsureRegistered()
    {
        // The static constructor is triggered by this call.
    }

    /// <summary>
    /// Resolves an encoding by name; returns <see langword="null"/> if it is not recognised.
    /// </summary>
    /// <remarks>
    /// A name coming from a user setting may be invalid. Returning <see langword="null"/> instead
    /// of throwing lets the caller fall back to the default — a diff must not go unshown entirely
    /// because of a single wrong setting.
    /// </remarks>
    public static Encoding? TryGet(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
