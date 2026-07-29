using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P03-T15 — Commit imza durumu, gerçek <c>git</c> ve gerçek SSH imzalarıyla.
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
            Assert.Skip("ssh-keygen bulunamadı; imzalama testi atlandı.");
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
        // Bu testin bütün varlık sebebi ölçülmüş bir tuzak: allowedSignersFile
        // yapılandırılmamışsa git, İMZALI bir commit için %G? alanında "N" döner ve
        // yalnızca stderr'e hata yazar. Ham %G?'ye güvenmek, imzalı bir commit'e
        // "imzasız" demek olurdu — kullanıcıya yanlış bilgi.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        if (!repository.TryEnableSshSigning())
        {
            Assert.Skip("ssh-keygen bulunamadı; imzalama testi atlandı.");
        }

        // TrustSigningKey ÇAĞRILMIYOR — tuzağın oluştuğu durum bu.
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
