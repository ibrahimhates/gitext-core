using GitExt.Core;

namespace GitExt.UI.ViewModels;

/// <summary>
/// What kind of tag is created (P14-T03).
/// </summary>
/// <remarks>
/// GitExtensions' <c>TagOperation</c> has a fourth entry, <i>Sign with specific GPG</i>; it needs a
/// key id and <see cref="TagOptions"/> carries no key, so it is left out rather than offered dead.
/// </remarks>
public enum TagKind
{
    Lightweight,
    Annotated,
    SignWithDefaultKey,
}

/// <summary>The context handed to the create tag dialog (P14-T03).</summary>
public sealed record CreateTagRequest
{
    /// <summary>The commit to tag; <c>HEAD</c> when <see langword="null"/>.</summary>
    public string? Target { get; init; }

    /// <summary>The description of the target to show the user.</summary>
    public required string TargetLabel { get; init; }

    /// <summary>Tag names already in the repository.</summary>
    public IReadOnlyList<string> ExistingTags { get; init; } = [];
}

/// <summary>The user's decision in the create tag dialog (P14-T03).</summary>
public sealed record CreateTagDecision
{
    public bool Confirmed { get; init; }

    public string Name { get; init; } = string.Empty;

    public TagKind Kind { get; init; } = TagKind.Lightweight;

    /// <summary>The annotation; only read for <see cref="TagKind.Lightweight"/>'s siblings.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary><c>--force</c>: move a tag of the same name.</summary>
    public bool Force { get; init; }

    public static CreateTagDecision Cancelled { get; } = new();
}

/// <summary>The context handed to the delete tag dialog (P14-T03).</summary>
public sealed record DeleteTagRequest
{
    /// <summary>The tag clicked in the submenu.</summary>
    public required string Name { get; init; }
}

/// <summary>The user's decision in the delete tag dialog (P14-T03).</summary>
public sealed record DeleteTagDecision
{
    public bool Confirmed { get; init; }

    public string Name { get; init; } = string.Empty;

    public static DeleteTagDecision Cancelled { get; } = new();
}

/// <summary>
/// The side that shows the tag dialogs (P14-T03).
/// </summary>
/// <remarks>
/// The same reasoning as <see cref="ICreateBranchPrompt"/>: a dialog needs an owner window and that
/// is only known on the view side.
/// </remarks>
public interface ITagPrompt
{
    /// <summary>GitExtensions' <c>FormCreateTag</c>.</summary>
    Task<CreateTagDecision> RequestCreateAsync(CreateTagRequest request);

    /// <summary>GitExtensions' <c>FormDeleteTag</c>.</summary>
    Task<DeleteTagDecision> RequestDeleteAsync(DeleteTagRequest request);
}

/// <summary>
/// Tag name rules (P14-T03).
/// </summary>
/// <remarks>
/// <para>
/// <c>refs/tags/</c> and <c>refs/heads/</c> obey the same <c>check-ref-format</c> rules, so
/// <see cref="BranchName.Validate"/> is reused; only the prefix differs.
/// </para>
/// <para>
/// 🔴 MEASURED — the leading dash matters more here than it does for a branch:
/// <c>git tag -lead</c> does not create a tag called <c>-lead</c>, it parses the name as the option
/// bundle <c>-l -e -a -d</c> and answered <c>error: options '-d' and '-l' cannot be used together</c>.
/// <c>TagWriter</c> appends the name positionally with no <c>--end-of-options</c>, so this rejection
/// is what keeps such a name from reaching git at all.
/// </para>
/// </remarks>
public static class TagNameRules
{
    private const string TagsPrefix = "refs/tags/";

    public static BranchNameProblem? Validate(string? name) =>
        name is not null && name.StartsWith(TagsPrefix, StringComparison.Ordinal)
            ? BranchNameProblem.NestedRefsPrefix
            : BranchName.Validate(name);

    public static bool IsValid(string? name) => Validate(name) is null;
}
