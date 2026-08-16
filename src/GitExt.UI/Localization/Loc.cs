using System.Globalization;
using GitExt.Core.Git;

namespace GitExt.UI.Localization;

/// <summary>
/// Access to translations from code (P11-T05).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>This is not a Service Locator — but the line has to be drawn here.</b>
/// ADR-0004 forbids the Service Locator, and its reasoning is this: when dependencies are hidden, what
/// a class needs cannot be worked out from its constructor. Translation is kept outside that rule,
/// because:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>It is a single, unchanging source across the application.</b> It has no behaviour that needs
///     faking for a test — it can be swapped with <see cref="UseForTesting"/>.
///   </item>
///   <item>
///     <b>The alternative has a real cost:</b> adding one more parameter to the constructors of 22
///     ViewModels meant changing all their call sites and their tests — while none of them would do
///     translation <i>differently</i>.
///   </item>
///   <item>
///     The same reasoning applies to <see cref="TranslateExtension"/>, where it was a technical
///     necessity (the markup extension is created by the XAML resolver).
///   </item>
/// </list>
/// <para>
/// <b>The rule:</b> this is consulted only for <b>text shown to the user</b>. No other service is
/// reached this way.
/// </para>
/// </remarks>
public static class Loc
{
    private static ITranslator? _translator;

    /// <summary>
    /// The text for a key. Falls back to built-in English when no translator is attached.
    /// </summary>
    /// <remarks>
    /// 🔴 This used to return the <b>key name</b> when no translator was attached, and any code
    /// path that runs without the composition root — plain <c>[Fact]</c> tests, the XAML
    /// designer — showed raw keys like <c>git_output.commit_created</c>. A test caught it.
    /// <see cref="BuiltInEnglish"/> is compiled in and depends on no file, so there is no
    /// reason to ever show a key to anyone.
    /// </remarks>
    public static string T(string key) =>
        _translator is not null
            ? _translator[key]
            : BuiltInEnglish.Entries.GetValueOrDefault(key, key);

    /// <summary>Fills in a placeholder text.</summary>
    public static string F(string key, params object?[] arguments)
    {
        if (_translator is not null)
        {
            return _translator.Format(key, arguments);
        }

        string template = T(key);

        if (arguments is not { Length: > 0 })
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            // A broken placeholder in a translation must not crash the application.
            return template;
        }
    }

    /// <summary>Registers the translator in force. Called once from the composition root.</summary>
    public static void Attach(ITranslator translator) => _translator = translator;

    /// <summary>For tests to swap the translator.</summary>
    internal static void UseForTesting(ITranslator? translator) => _translator = translator;

    /// <summary>
    /// The text to show the user for a git error (P11-T06).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GitExt.Core"/> cannot depend on the UI layer (ADR-0003), so it cannot reach the
    /// language file either. But <see cref="GitException.Kind"/> is already filled in by the
    /// classification layer: the translation looks at <b>that enum, not at the text</b>.
    /// </para>
    /// <para>
    /// <see cref="GitFailureKind.Unknown"/> <b>falls back to the raw message.</b> Hiding an unknown git
    /// error behind an invented text would make diagnosis impossible — both the user and we have to
    /// see what git said.
    /// </para>
    /// </remarks>
    public static string GitError(GitException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string key = exception.Kind switch
        {
            GitFailureKind.NotARepository => "git.error.not_a_repository",
            GitFailureKind.AuthenticationRequired => "git.error.authentication_required",
            GitFailureKind.NetworkFailure => "git.error.network_failure",
            GitFailureKind.IndexLocked => "git.error.index_locked",
            GitFailureKind.Conflict => "git.error.conflict",
            GitFailureKind.UnknownRevision => "git.error.unknown_revision",
            GitFailureKind.DirtyWorkingTree => "git.error.dirty_working_tree",
            GitFailureKind.Timeout => "git.error.timeout",
            GitFailureKind.BranchAlreadyExists => "git.error.branch_already_exists",
            GitFailureKind.RefNameConflict => "git.error.ref_name_conflict",
            GitFailureKind.UnbornHead => "git.error.unborn_head",
            GitFailureKind.RemoteAlreadyExists => "git.error.remote_already_exists",
            GitFailureKind.RemoteNotFound => "git.error.remote_not_found",
            GitFailureKind.RemoteNameConflict => "git.error.remote_name_conflict",
            GitFailureKind.RemoteUnreachable => "git.error.remote_unreachable",

            // An unclassified error: git's own message is shown.
            _ => string.Empty,
        };

        return key.Length == 0 ? exception.Message : T(key);
    }
}
