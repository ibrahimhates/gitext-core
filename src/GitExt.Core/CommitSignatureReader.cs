using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Reads a single commit's signature status (P03-T15).
/// </summary>
/// <remarks>
/// The reason for a separate reader is cost: adding signature verification to the bulk <c>git log</c>
/// read slows the history down (see <see cref="CommitSignatureInfo"/>).
/// </remarks>
public interface ICommitSignatureReader
{
    Task<CommitSignatureInfo> ReadAsync(
        string workingDirectory,
        CommitId commit,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICommitSignatureReader"/>
public sealed class CommitSignatureReader : ICommitSignatureReader
{
    private readonly IGitProcessRunner _runner;

    public CommitSignatureReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    private const string Format = "%G?%x00%GS%x00%GK";

    private const int FieldCount = 3;

    public async Task<CommitSignatureInfo> ReadAsync(
        string workingDirectory,
        CommitId commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (commit.IsEmpty)
        {
            return CommitSignatureInfo.Unsigned;
        }

        GitResult result = await _runner.RunCheckedAsync(
            GitCommand.Create(
                workingDirectory,
                "log",
                "-1",
                $"--format={Format}",
                commit.Value,
                // The separator: keeps the revision from being interpreted as a file path.
                "--"),
            cancellationToken).ConfigureAwait(false);

        string[] fields = result.SplitStandardOutputAtNulPreservingEmpty();

        if (fields.Length < FieldCount)
        {
            return CommitSignatureInfo.Unsigned;
        }

        return Build(fields, result.StandardError);
    }

    private static CommitSignatureInfo Build(string[] fields, string standardError)
    {
        // Because %GK is the last field it can carry the record separator at its end.
        string code = fields[0].Trim();
        string signer = fields[1];
        string key = fields[2].Trim('\n', '\r', '\0');

        SignatureStatus status = code switch
        {
            "G" => SignatureStatus.Valid,
            "U" => SignatureStatus.ValidUntrusted,
            "B" => SignatureStatus.Bad,
            "X" => SignatureStatus.Expired,
            "Y" => SignatureStatus.KeyExpired,
            "R" => SignatureStatus.KeyRevoked,
            "E" => SignatureStatus.CannotVerify,
            _ => SignatureStatus.None,
        };

        // MEASURED: with no allowedSignersFile configured, git returns "N" (unsigned) in the %G? field
        // for an SSH-signed commit and writes the error only to stderr. Without making this distinction
        // we would be calling a signed commit "unsigned".
        string? reason = DescribeVerifierFailure(standardError);

        if (reason is not null && status is SignatureStatus.None or SignatureStatus.CannotVerify)
        {
            return new CommitSignatureInfo
            {
                Status = SignatureStatus.CannotVerify,
                Signer = signer,
                Key = key,
                CannotVerifyReason = reason,
            };
        }

        return status == SignatureStatus.None
            ? CommitSignatureInfo.Unsigned
            : new CommitSignatureInfo { Status = status, Signer = signer, Key = key };
    }

    /// <summary>
    /// Is stderr saying the verifier could not run?
    /// </summary>
    /// <remarks>
    /// Looking at the text is fragile, but there is no alternative: git does not report this state via
    /// the exit code or via <c>%G?</c>. If the match fails, the result is "unsigned" as before — so not
    /// a regression, just a missed improvement.
    /// </remarks>
    private static string? DescribeVerifierFailure(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return null;
        }

        if (standardError.Contains("allowedSignersFile", StringComparison.OrdinalIgnoreCase))
        {
            return "gpg.ssh.allowedSignersFile is not configured; SSH signatures cannot be verified.";
        }

        if (standardError.Contains("gpg", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("signature", StringComparison.OrdinalIgnoreCase))
        {
            return standardError.Trim();
        }

        return null;
    }
}
