using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Tek bir commit'in imza durumunu okur (P03-T15).
/// </summary>
/// <remarks>
/// Ayrı bir okuyucu olmasının sebebi maliyet: imza doğrulaması toplu <c>git log</c>
/// okumasına eklendiğinde geçmişi yavaşlatıyor (bkz. <see cref="CommitSignatureInfo"/>).
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
                // Ayırıcı: revizyonun dosya yolu olarak yorumlanmasını engeller.
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
        // %GK son alan olduğu için sonunda kayıt sonu karakteri taşıyabilir.
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

        // ÖLÇÜLDÜ: allowedSignersFile yapılandırılmamışken git, SSH imzalı bir commit için
        // %G? alanında "N" (imzasız) döner ve yalnızca stderr'e hata yazar. Bu ayrımı
        // yapmazsak imzalı bir commit'e "imzasız" demiş oluruz.
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
    /// stderr doğrulayıcının çalışamadığını mı söylüyor?
    /// </summary>
    /// <remarks>
    /// Metne bakmak kırılgandır ama alternatifi yok: git bu durumu çıkış kodu veya
    /// <c>%G?</c> ile bildirmiyor. Eşleşme başarısız olursa sonuç eskisi gibi "imzasız"
    /// olur — yani kötüleşme değil, sadece iyileştirmenin kaçırılması.
    /// </remarks>
    private static string? DescribeVerifierFailure(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return null;
        }

        if (standardError.Contains("allowedSignersFile", StringComparison.OrdinalIgnoreCase))
        {
            return "gpg.ssh.allowedSignersFile yapılandırılmamış; SSH imzaları doğrulanamıyor.";
        }

        if (standardError.Contains("gpg", StringComparison.OrdinalIgnoreCase)
            || standardError.Contains("signature", StringComparison.OrdinalIgnoreCase))
        {
            return standardError.Trim();
        }

        return null;
    }
}
