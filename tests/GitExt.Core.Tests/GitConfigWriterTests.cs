using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P08-T15 — writing <c>git config</c>, <b>against real git</b>.
/// </summary>
/// <remarks>
/// The tests use only the local scope: the global scope would write into the user's <c>~/.gitconfig</c>
/// file, and a test must <b>never</b> change the developer's real
/// configuration.
/// </remarks>
public class GitConfigWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(GitConfigWriter Writer, GitConfigReader Reader)> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);
        GitProcessRunner runner = new(executable);
        GitWriteQueue queue = new();

        return (new GitConfigWriter(new GitWriter(runner, queue), runner), new GitConfigReader(runner));
    }

    [Fact]
    public async Task Yerel_ayar_yazilip_okunuyor()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, GitConfigReader reader) = await CreateAsync();

        await writer.SetAsync(repository.Path, "user.name", "Ayşe Yılmaz", GitConfigScope.Local, Ct);

        (await reader.GetAsync(repository.Path, "user.name", Ct)).ShouldBe("Ayşe Yılmaz");
        (await writer.GetScopedAsync(repository.Path, "user.name", GitConfigScope.Local, Ct))
            .ShouldBe("Ayşe Yılmaz");
    }

    /// <summary>
    /// 🔴 An empty value <b>deletes</b> the setting, it does not set it to empty.
    /// </summary>
    /// <remarks>
    /// MEASURED: <c>git config user.name ""</c> gives exit code 0 and the setting ends up <b>present but
    /// empty</b>. Committing with an empty <c>user.name</c> produces a different and worse error than
    /// having it never set at all. This test protects the "delete" intent of the user who clears
    /// the field.
    /// </remarks>
    [Fact]
    public async Task Bos_deger_ayari_SILIYOR()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, GitConfigReader reader) = await CreateAsync();

        await writer.SetAsync(repository.Path, "gitext.probe", "değer", GitConfigScope.Local, Ct);
        await writer.SetAsync(repository.Path, "gitext.probe", "", GitConfigScope.Local, Ct);

        (await reader.GetAsync(repository.Path, "gitext.probe", Ct)).ShouldBeNull();
        (await writer.GetScopedAsync(repository.Path, "gitext.probe", GitConfigScope.Local, Ct))
            .ShouldBeNull("ayar silinmeli, boş dizeye ayarlanmamalı");
    }

    /// <summary>
    /// 🔴 Deleting a setting that does not exist is <b>not an error</b>.
    /// </summary>
    /// <remarks>
    /// MEASURED: <c>git config --unset</c> gives <b>exit code 5</b> on a missing key —
    /// neither 0 nor 1. Had it been treated as an error, a user clearing a field that was already empty
    /// would see an error while nothing had gone wrong.
    /// </remarks>
    [Fact]
    public async Task Olmayan_ayari_silmek_hata_degil()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, _) = await CreateAsync();

        await Should.NotThrowAsync(() =>
            writer.SetAsync(repository.Path, "gitext.hicYokBu", "", GitConfigScope.Local, Ct));
    }

    /// <summary>
    /// A scoped read reads <b>that file</b>, not the merged result.
    /// </summary>
    /// <remarks>
    /// The distinction is essential: a merged read does not tell you which file the value came from.
    /// Showing a global value in the local field meant the user unknowingly creating a local copy when
    /// they saved.
    /// </remarks>
    [Fact]
    public async Task Kapsamli_okuma_birlesimi_degil_o_dosyayi_okuyor()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, GitConfigReader reader) = await CreateAsync();

        // The fixture sets a local `user.email`; we remove it and see that the local scope really is
        // empty.
        await writer.SetAsync(repository.Path, "user.email", "", GitConfigScope.Local, Ct);

        (await writer.GetScopedAsync(repository.Path, "user.email", GitConfigScope.Local, Ct))
            .ShouldBeNull();

        // A merged read may still find a value (the developer's global setting); this test does not
        // look at what that is, but at the LOCAL scope being read separately.
        await writer.SetAsync(repository.Path, "user.email", "yerel@örnek", GitConfigScope.Local, Ct);

        (await reader.GetAsync(repository.Path, "user.email", Ct)).ShouldBe("yerel@örnek");
    }

    [Fact]
    public async Task Ayarlanmamis_anahtar_null_donuyor()
    {
        using TestRepository repository = TestRepository.CreateEmpty();
        (GitConfigWriter writer, _) = await CreateAsync();

        (await writer.GetScopedAsync(repository.Path, "gitext.yok", GitConfigScope.Local, Ct))
            .ShouldBeNull();
    }

    /// <summary>
    /// A local read in a directory that is not a repository <b>does not crash</b>.
    /// </summary>
    /// <remarks>
    /// MEASURED: <c>git config --local</c> outside a repository gives <c>fatal</c> and exit code <b>128</b>.
    /// A directory given on the command line may not be a repository; throwing for that would make the
    /// application impossible to open.
    /// </remarks>
    [Fact]
    public async Task Depo_olmayan_dizinde_yerel_okuma_cokmuyor()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gitext-nonrepo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);

        try
        {
            (GitConfigWriter writer, _) = await CreateAsync();

            (await writer.GetScopedAsync(directory, "user.name", GitConfigScope.Local, Ct))
                .ShouldBeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
