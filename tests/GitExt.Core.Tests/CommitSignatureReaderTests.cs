using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P03-T15 — Commit signature status, with real <c>git</c> and real SSH signatures.
/// </summary>
public class CommitSignatureReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<CommitSignatureReader> CreateReaderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new CommitSignatureReader(new GitProcessRunner(executable));
    }

    private static CommitId HeadOf(TestRepository repository) =>
        CommitId.Parse(repository.Git("rev-parse", "HEAD").Trim());

    [Fact]
    public async Task Imzasiz_commit_imzasiz_raporlanir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        CommitSignatureReader reader = await CreateReaderAsync();

        CommitSignatureInfo signature = await reader.ReadAsync(repository.Path, HeadOf(repository), Ct);

        signature.Status.ShouldBe(SignatureStatus.None);
        signature.IsSigned.ShouldBeFalse();
        signature.IsTrusted.ShouldBeFalse();
    }

    [Fact]
    public async Task Guvenilen_anahtarla_imzali_commit_gecerli_raporlanir()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        if (!repository.TryEnableSshSigning())
        {
            Assert.Skip("SSH imzalama kurulamadı (ssh-keygen yok ya da git < 2.34); imzalama testi atlandı.");
        }

        repository.TrustSigningKey();
        repository.Git("commit", "--allow-empty", "-S", "-m", "imzalı");

        CommitSignatureReader reader = await CreateReaderAsync();

        CommitSignatureInfo signature = await reader.ReadAsync(repository.Path, HeadOf(repository), Ct);

        signature.Status.ShouldBe(SignatureStatus.Valid);
        signature.IsSigned.ShouldBeTrue();
        signature.IsTrusted.ShouldBeTrue();
        signature.Signer.ShouldBe("tests@gitext-core.invalid");
        signature.Key.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Guvenilenler_listesi_yokken_imzali_commit_imzasiz_SAYILMAZ()
    {
        // The entire reason this test exists is a measured trap: if allowedSignersFile
        // is not configured, git returns "N" in the %G? field for a SIGNED commit and
        // only writes the error to stderr. Trusting the raw %G? would mean calling a signed
        // commit "unsigned" — wrong information for the user.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        if (!repository.TryEnableSshSigning())
        {
            Assert.Skip("SSH imzalama kurulamadı (ssh-keygen yok ya da git < 2.34); imzalama testi atlandı.");
        }

        // TrustSigningKey is NOT CALLED — this is the situation where the trap occurs.
        repository.Git("commit", "--allow-empty", "-S", "-m", "imzalı ama doğrulanamaz");

        CommitSignatureReader reader = await CreateReaderAsync();

        CommitSignatureInfo signature = await reader.ReadAsync(repository.Path, HeadOf(repository), Ct);

        signature.Status.ShouldBe(SignatureStatus.CannotVerify);
        signature.IsSigned.ShouldBeTrue();
        signature.IsTrusted.ShouldBeFalse();
        signature.CannotVerifyReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Bos_commit_kimligi_git_calistirmadan_imzasiz_doner()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        CommitSignatureReader reader = await CreateReaderAsync();

        CommitSignatureInfo signature = await reader.ReadAsync(repository.Path, default, Ct);

        signature.Status.ShouldBe(SignatureStatus.None);
    }
}
