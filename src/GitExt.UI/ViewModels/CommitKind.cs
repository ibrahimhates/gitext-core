using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

public enum CommitKind
{
    Normal,
    Fixup,
    Squash,
    Amend,
}

public static class CommitKindMessage
{
    public static string Prefix(CommitKind kind) => kind switch
    {
        CommitKind.Fixup => "fixup!",
        CommitKind.Squash => "squash!",
        CommitKind.Amend => "amend!",
        _ => string.Empty,
    };

    public static string For(CommitKind kind, CommitInfo target)
    {
        ArgumentNullException.ThrowIfNull(target);

        string subject = WithPrefix(kind, target.Subject);

        return kind == CommitKind.Amend
            ? $"{subject}\n\n{target.Body}"
            : subject;
    }

    public static bool LocksMessage(CommitKind kind) => kind is CommitKind.Fixup or CommitKind.Squash;

    private static string WithPrefix(CommitKind kind, string subject)
    {
        string prefix = Prefix(kind);

        return prefix.Length == 0 || subject.StartsWith(prefix, StringComparison.Ordinal)
            ? subject
            : $"{prefix} {subject}";
    }
}
