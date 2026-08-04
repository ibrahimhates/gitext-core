using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P06-T04 — ayrık (detached) HEAD ve süregelen işlemler.
/// </summary>
public class DetachedHeadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        TestRepository Repository,
        StatusReader Status,
        InProgressOperationReader Operations) : IDisposable
    {
        public void Dispose() => Repository.Dispose();

        public string Path => Repository.Path;
    }

    private static async Task<Harness> CreateAsync()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);

        repository.WriteFile("a.txt", "bir\n");
        repository.Git("add", "-A");
        repository.Commit("ilk");
        repository.WriteFile("b.txt", "b\n");
        repository.Git("add", "-A");
        repository.Commit("ikinci");

        return new Harness(repository, new StatusReader(runner), new InProgressOperationReader(runner));
    }

    [Fact]
    public async Task Gercek_ayrik_HEAD_bildiriliyor()
    {
        using Harness harness = await CreateAsync();
        harness.Repository.Git("switch", "--detach", "HEAD~1");

        WorkingTreeStatus status = await harness.Status.ReadAsync(harness.Path, cancellationToken: Ct);

        status.IsDetached.ShouldBeTrue();
        status.BranchName.ShouldBeNull();
    }

    [Fact]
    public async Task Detached_ADLI_dal_ayrik_HEAD_SANILMIYOR()
    {
        // 🔴 ÖLÇÜLDÜ: "(detached)" GEÇERLİ bir dal adı — `check-ref-format --branch` kabul
        // ediyor, `git branch "(detached)"` gerçekten oluşturuyor. O dalın üzerindeyken
        // `--porcelain=v2` yine "# branch.head (detached)" yazıyor, yani çıktı ayırt
        // EDİLEMİYOR. Kullanıcı bir dalın üzerindeyken "ayrık HEAD, commit'leriniz
        // kaybolabilir" uyarısı alırdı.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("switch", "-c", "(detached)");

        WorkingTreeStatus status = await harness.Status.ReadAsync(harness.Path, cancellationToken: Ct);

        status.IsDetached.ShouldBeFalse();
        status.BranchName.ShouldBe("(detached)");
    }

    [Fact]
    public async Task Normal_dalda_ayrik_bildirilmiyor()
    {
        using Harness harness = await CreateAsync();

        WorkingTreeStatus status = await harness.Status.ReadAsync(harness.Path, cancellationToken: Ct);

        status.IsDetached.ShouldBeFalse();
        status.BranchName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Temiz_depoda_suren_islem_YOK()
    {
        using Harness harness = await CreateAsync();

        (await harness.Operations.ReadAsync(harness.Path, Ct))
            .ShouldBe(InProgressOperation.None);
    }

    [Fact]
    public async Task Rebase_sirasinda_HEAD_ayrik_ama_ISLEM_bildiriliyor()
    {
        // 🔴 ÖLÇÜLDÜ: rebase sırasında HEAD gerçekten ayrık. Düz bir "ayrık HEAD" uyarısı
        // burada da açılırdı; oysa kullanıcı bilerek bir işlemin ortasında ve ona
        // söylenmesi gereken şey "buradan dal oluştur" değil, hangi işlemin sürdüğü.
        using Harness harness = await CreateAsync();

        harness.Repository.Git("switch", "-c", "dal");
        harness.Repository.WriteFile("a.txt", "dalda\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("dal değişikliği");

        harness.Repository.Git("switch", "-");
        harness.Repository.WriteFile("a.txt", "mainde\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("main değişikliği");

        harness.Repository.Git("switch", "dal");
        harness.Repository.TryGit("rebase", "main");

        WorkingTreeStatus status = await harness.Status.ReadAsync(harness.Path, cancellationToken: Ct);

        status.IsDetached.ShouldBeTrue();

        (await harness.Operations.ReadAsync(harness.Path, Ct))
            .ShouldBe(InProgressOperation.Rebase);

        harness.Repository.TryGit("rebase", "--abort");
    }

    [Fact]
    public async Task Bisect_sirasinda_ISLEM_bildiriliyor()
    {
        // ÖLÇÜLDÜ: bisect de HEAD'i ayırıyor.
        using Harness harness = await CreateAsync();

        for (int i = 1; i <= 4; i++)
        {
            harness.Repository.WriteFile($"f{i}.txt", $"{i}\n");
            harness.Repository.Git("add", "-A");
            harness.Repository.Commit($"c{i}");
        }

        harness.Repository.TryGit("bisect", "start", "HEAD", "HEAD~4");

        (await harness.Operations.ReadAsync(harness.Path, Ct))
            .ShouldBe(InProgressOperation.Bisect);

        harness.Repository.TryGit("bisect", "reset");
    }

    [Fact]
    public async Task Merge_cakismasi_ISLEM_olarak_bildiriliyor()
    {
        using Harness harness = await CreateAsync();

        harness.Repository.Git("switch", "-c", "dal");
        harness.Repository.WriteFile("a.txt", "dalda\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("dal");

        harness.Repository.Git("switch", "-");
        harness.Repository.WriteFile("a.txt", "mainde\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("main");

        harness.Repository.TryGit("merge", "dal");

        (await harness.Operations.ReadAsync(harness.Path, Ct))
            .ShouldBe(InProgressOperation.Merge);

        harness.Repository.TryGit("merge", "--abort");
    }

    [Fact]
    public async Task Ayrik_HEAD_te_atilan_commit_reflog_ta_KALIYOR()
    {
        // Uyarının tonu buna bağlı: içerik anında kaybolmuyor, ama hiçbir dalda görünmüyor.
        // "Kaybettiniz" demek yanlış, "hiçbir dal göstermiyor" demek doğru.
        using Harness harness = await CreateAsync();
        harness.Repository.Git("switch", "--detach", "HEAD~1");
        harness.Repository.WriteFile("x.txt", "ayrık işi\n");
        harness.Repository.Git("add", "-A");
        harness.Repository.Commit("AYRIK COMMIT");

        string sha = harness.Repository.Git("rev-parse", "HEAD").Trim();

        harness.Repository.Git("switch", "-");

        harness.Repository.Git("reflog").ShouldContain(sha[..7]);
        harness.Repository.Git("cat-file", "-t", sha).Trim().ShouldBe("commit");
    }

    [Theory]
    [InlineData("rebase-merge", true, InProgressOperation.Rebase)]
    [InlineData("CHERRY_PICK_HEAD", false, InProgressOperation.CherryPick)]
    [InlineData("REVERT_HEAD", false, InProgressOperation.Revert)]
    [InlineData("MERGE_HEAD", false, InProgressOperation.Merge)]
    [InlineData("BISECT_LOG", false, InProgressOperation.Bisect)]
    public void Durum_dosyalari_DOGRU_isleme_esleniyor(
        string name,
        bool isDirectory,
        InProgressOperation expected)
    {
        // Saf sınıflandırma: her durumu gerçek git ile kurmak yavaş ve bazıları (revert
        // çakışması) kurulumu kırılgan. Gerçek git ile kurulabilenler yukarıda ayrıca test
        // ediliyor; bu tablo eşlemenin tamamını sabitliyor.
        string root = Directory.CreateTempSubdirectory("gitext-op").FullName;

        try
        {
            string target = Path.Combine(root, name);

            if (isDirectory)
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                File.WriteAllText(target, string.Empty);
            }

            InProgressOperationReader.Classify(root).ShouldBe(expected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void rebase_apply_git_am_den_AYIRT_ediliyor()
    {
        // `rebase-apply` hem `rebase --apply` hem `git am` tarafından kullanılıyor;
        // ayrımı içindeki `applying` dosyası veriyor.
        string root = Directory.CreateTempSubdirectory("gitext-op").FullName;

        try
        {
            string apply = Path.Combine(root, "rebase-apply");
            Directory.CreateDirectory(apply);

            InProgressOperationReader.Classify(root).ShouldBe(InProgressOperation.Rebase);

            File.WriteAllText(Path.Combine(apply, "applying"), string.Empty);

            InProgressOperationReader.Classify(root).ShouldBe(InProgressOperation.ApplyMailbox);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
