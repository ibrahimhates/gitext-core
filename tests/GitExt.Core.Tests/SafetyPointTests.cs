using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P07-T15 — işlem öncesi güvenlik noktası.
/// </summary>
/// <remarks>
/// Faz kuralı: geçmişi değiştiren her işlem öncesinde konum kaydedilir ve "nasıl geri
/// alırım" bilgisi <b>her zaman</b> sunulur.
/// </remarks>
public class SafetyPointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SafetyPointRecorder> CreateRecorderAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        return new SafetyPointRecorder(new GitProcessRunner(executable));
    }

    [Fact]
    public async Task Dal_uzerindeyken_dal_adi_ve_reset_komutu_veriliyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        SafetyPoint point = await (await CreateRecorderAsync()).CaptureAsync(repository.Path, "rebase", Ct);

        point.BranchName.ShouldBe("main");
        point.IsDetached.ShouldBeFalse();
        point.Operation.ShouldBe("rebase");
        point.ObjectId.ShouldBe(repository.Git("rev-parse", "HEAD").Trim());
        point.RecoveryCommand.ShouldBe($"git reset --hard {point.ObjectId}");
    }

    [Fact]
    public async Task AYRIK_HEADde_checkout_oneriliyor()
    {
        // ⚠️ ÖLÇÜLDÜ: ayrık HEAD'de `reset --hard` hiçbir dalı oynatmıyor; kullanıcının
        // istediği şey o commit'e dönmek olduğu için `checkout` doğru komut.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.Commit("ikinci");
        repository.Git("checkout", "--detach", "HEAD");

        SafetyPoint point = await (await CreateRecorderAsync()).CaptureAsync(repository.Path, "reset", Ct);

        point.IsDetached.ShouldBeTrue();
        point.BranchName.ShouldBeNull();
        point.RecoveryCommand.ShouldBe($"git checkout {point.ObjectId}");
    }

    [Fact]
    public async Task Temiz_agac_TAM_geri_alinabilir_sayiliyor()
    {
        using TestRepository repository = TestRepository.CreateWithSingleCommit();

        SafetyPoint point = await (await CreateRecorderAsync()).CaptureAsync(repository.Path, "reset", Ct);

        point.HasUncommittedChanges.ShouldBeFalse();
        point.IsFullyRecoverable.ShouldBeTrue();
    }

    [Fact]
    public async Task KIRLI_agacta_geri_almanin_EKSIK_oldugu_soyleniyor()
    {
        // 🔴 ÖLÇÜLDÜ: `git reset --hard <sha>` commit'lenmemiş işi de siliyor. "Geri almak
        // için: git reset --hard <sha>" demek, ağaç kirliyken EKSİK bir söz — commit geri
        // gelir, kullanıcının o anki işi gelmez.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.WriteFile("README.md", "# degisti\n");

        SafetyPoint point = await (await CreateRecorderAsync()).CaptureAsync(repository.Path, "rebase", Ct);

        point.HasUncommittedChanges.ShouldBeTrue();
        point.IsFullyRecoverable.ShouldBeFalse();
    }

    [Fact]
    public async Task TAKIP_EDILMEYEN_dosya_geri_alinabilirligi_ETKILEMIYOR()
    {
        // ÖLÇÜLDÜ: takip edilmeyen dosyalar `reset --hard`'ı atlatıyor — commit'lerden
        // sonra oluşturulan `takipsiz.txt` reset sonrası diskte DURUYORDU. Dolayısıyla
        // onları "kirli" sayıp kullanıcıyı boşuna uyarmak yanlış olurdu.
        using TestRepository repository = TestRepository.CreateWithSingleCommit();
        repository.WriteFile("takipsiz.txt", "x\n");

        SafetyPoint point = await (await CreateRecorderAsync()).CaptureAsync(repository.Path, "rebase", Ct);

        point.HasUncommittedChanges.ShouldBeFalse();
        point.IsFullyRecoverable.ShouldBeTrue();
    }

    [Fact]
    public async Task Dogmamis_depoda_COKMUYOR()
    {
        using TestRepository repository = TestRepository.CreateEmpty();

        SafetyPoint point = await (await CreateRecorderAsync()).CaptureAsync(repository.Path, "reset", Ct);

        point.ObjectId.ShouldBeEmpty();
    }

    [Fact]
    public void Geri_alma_komutu_KAYAN_referans_icermiyor()
    {
        SafetyPoint point = new()
        {
            ObjectId = "0123456789abcdef0123456789abcdef01234567",
            BranchName = "main",
            Operation = "rebase",
        };

        point.RecoveryCommand.ShouldNotContain("ORIG_HEAD");
        point.RecoveryCommand.ShouldNotContain("@{");
        point.ShortId.ShouldBe("0123456");
    }
}
