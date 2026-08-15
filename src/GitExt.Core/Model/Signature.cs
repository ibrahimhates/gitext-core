namespace GitExt.Core.Model;

/// <summary>
/// The author or committer of a commit: who, and when.
/// </summary>
/// <param name="Name">Display name. May be empty — git does not require it.</param>
/// <param name="Email">Email address. May be empty.</param>
/// <param name="When">
/// Timestamp, <b>together with the original time zone offset</b>.
/// </param>
/// <remarks>
/// <see cref="DateTimeOffset"/> is used because the time zone a commit was made in is
/// meaningful information and may need to be shown. Converting to <see cref="DateTime"/> loses it.
/// </remarks>
public sealed record Signature(string Name, string Email, DateTimeOffset When)
{
    /// <summary>For display only: <c>Name &lt;email&gt;</c>.</summary>
    public override string ToString() =>
        string.IsNullOrEmpty(Email) ? Name : $"{Name} <{Email}>";
}
