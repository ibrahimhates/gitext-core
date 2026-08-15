namespace GitExt.Core.Model;

/// <summary>
/// The verification result of a commit signature (P03-T15).
/// </summary>
/// <remarks>
/// The counterparts of the <c>git log --format=%G?</c> field. One of them does not come from
/// git directly: <see cref="CannotVerify"/> represents the case where git could not run the
/// verifier at all — see <see cref="CommitSignatureInfo"/>.
/// </remarks>
public enum SignatureStatus
{
    /// <summary>No signature (<c>N</c>).</summary>
    None,

    /// <summary>Valid signature (<c>G</c>).</summary>
    Valid,

    /// <summary>Signature is valid but the key is not marked trusted (<c>U</c>).</summary>
    ValidUntrusted,

    /// <summary>Signature is <b>bad</b> (<c>B</c>) — the content may have changed after signing.</summary>
    Bad,

    /// <summary>The signature has expired (<c>X</c>).</summary>
    Expired,

    /// <summary>The signing key has expired (<c>Y</c>).</summary>
    KeyExpired,

    /// <summary>The signing key was revoked (<c>R</c>).</summary>
    KeyRevoked,

    /// <summary>Verification could not be performed (<c>E</c>, or configuration missing).</summary>
    CannotVerify,
}

/// <summary>
/// The signature information of a commit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why is this not in the bulk <c>git log</c> read?</b> Measured: on 2,000 <b>unsigned</b>
/// commits, adding <c>%G?</c> to the format raises the read from 12.1 ms to 20.8 ms (+72%). On
/// really signed commits the gap grows far larger, since each line triggers a cryptographic
/// verification. Because the details panel shows one commit at a time, the signature is read
/// <b>separately, for the selected commit</b>.
/// </para>
/// <para>
/// <b>⚠️ <c>%G?</c> on its own is misleading.</b> Measured: an SSH-signed commit returns <c>N</c>
/// — i.e. "unsigned" — when <c>gpg.ssh.allowedSignersFile</c> is not configured; git only writes
/// an error to stderr. Telling the user "unsigned" for a signed commit is wrong information, so
/// stderr is inspected as well and the <see cref="SignatureStatus.CannotVerify"/> distinction
/// is made.
/// </para>
/// </remarks>
public sealed record CommitSignatureInfo
{
    /// <summary>Ready-made value for a commit without a signature.</summary>
    public static CommitSignatureInfo Unsigned { get; } = new() { Status = SignatureStatus.None };

    public required SignatureStatus Status { get; init; }

    /// <summary>Signer's name/email (<c>%GS</c>); empty when unknown.</summary>
    public string Signer { get; init; } = string.Empty;

    /// <summary>Id of the signing key (<c>%GK</c>); empty when unknown.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Why could verification not be performed? Filled only in the
    /// <see cref="SignatureStatus.CannotVerify"/> case.
    /// </summary>
    public string? CannotVerifyReason { get; init; }

    /// <summary>Is the commit signed? A signature that cannot be verified is a signature too.</summary>
    public bool IsSigned => Status != SignatureStatus.None;

    /// <summary>
    /// Can the signature be considered trusted?
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> only for <see cref="SignatureStatus.Valid"/>. A signature whose trust
    /// is not marked (<c>U</c>) must not be presented as "verified".
    /// </remarks>
    public bool IsTrusted => Status == SignatureStatus.Valid;

    public override string ToString() => Status.ToString();
}
