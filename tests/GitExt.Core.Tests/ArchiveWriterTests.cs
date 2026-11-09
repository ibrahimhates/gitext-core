using System.Formats.Tar;
using System.IO.Compression;
using GitExt.Core.Git;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

public sealed class ArchiveWriterTests : IDisposable
{
    private readonly string _output = Path.Combine(Path.GetTempPath(), "gitext-archive-" + Guid.NewGuid().ToString("N"));

    public ArchiveWriterTests() => Directory.CreateDirectory(_output);

    public void Dispose() => Directory.Delete(_output, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ArchiveWriter> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct);

        return new ArchiveWriter(new GitProcessRunner(executable));
    }

    private static (TestRepository Repository, string First, string Second) TwoRevisions()
    {
        TestRepository repository = TestRepository.CreateEmpty();
        repository.WriteFile("a dir/x y.txt", "1\n");
        repository.WriteFile("gone.txt", "1\n");
        repository.WriteFile("keep.txt", "1\n");
        repository.WriteFile("st*r.txt", "1\n");
        repository.WriteFile("stXr.txt", "1\n");
        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "first");
        string first = repository.Git("rev-parse", "HEAD").Trim();

        repository.WriteFile("a dir/x y.txt", "2\n");
        repository.WriteFile("st*r.txt", "2\n");
        repository.WriteFile("new.txt", "n\n");
        repository.Git("rm", "-q", "gone.txt");
        repository.Git("add", "-A");
        repository.Git("commit", "-q", "-m", "second");

        return (repository, first, repository.Git("rev-parse", "HEAD").Trim());
    }

    private static string[] ZipEntries(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        return [.. zip.Entries.Select(e => e.FullName).Where(name => !name.EndsWith('/')).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task Zip_tum_agaci_iceriyor()
    {
        (TestRepository repository, _, string second) = TwoRevisions();
        using TestRepository owned = repository;
        string file = Path.Combine(_output, "all.zip");

        await (await CreateAsync()).ArchiveAsync(repository.Path, new ArchiveOptions { Revision = second, OutputPath = file }, Ct);

        ZipEntries(file).ShouldBe(["a dir/x y.txt", "keep.txt", "new.txt", "st*r.txt", "stXr.txt"]);
    }

    [Fact]
    public async Task Tar_bicimi()
    {
        (TestRepository repository, _, string second) = TwoRevisions();
        using TestRepository owned = repository;
        string file = Path.Combine(_output, "all.tar");

        await (await CreateAsync()).ArchiveAsync(
            repository.Path,
            new ArchiveOptions { Revision = second, OutputPath = file, Format = ArchiveFormat.Tar },
            Ct);

        await using FileStream stream = File.OpenRead(file);
        using TarReader reader = new(stream);
        List<string> names = [];

        while (await reader.GetNextEntryAsync(cancellationToken: Ct) is { } entry)
        {
            names.Add(entry.Name);
        }

        names.ShouldContain("keep.txt");
    }

    [Fact]
    public async Task Yalniz_degisenler_silinmisi_ve_degismeyeni_almiyor()
    {
        (TestRepository repository, string first, string second) = TwoRevisions();
        using TestRepository owned = repository;
        string file = Path.Combine(_output, "changed.zip");

        await (await CreateAsync()).ArchiveAsync(
            repository.Path,
            new ArchiveOptions { Revision = second, OutputPath = file, ChangedSince = first },
            Ct);

        // `st*r.txt` changed, `stXr.txt` did not: a plain pathspec would glob-match both (measured).
        ZipEntries(file).ShouldBe(["a dir/x y.txt", "new.txt", "st*r.txt"]);
    }

    [Fact]
    public async Task Belirli_yollar()
    {
        (TestRepository repository, _, string second) = TwoRevisions();
        using TestRepository owned = repository;
        string file = Path.Combine(_output, "paths.zip");

        await (await CreateAsync()).ArchiveAsync(
            repository.Path,
            new ArchiveOptions { Revision = second, OutputPath = file, Paths = ["a dir", "keep.txt"] },
            Ct);

        ZipEntries(file).ShouldBe(["a dir/x y.txt", "keep.txt"]);
    }

    [Fact]
    public async Task Degisen_yoksa_dosya_yazilmiyor()
    {
        (TestRepository repository, _, string second) = TwoRevisions();
        using TestRepository owned = repository;
        string file = Path.Combine(_output, "empty.zip");

        ArchiveWriter writer = await CreateAsync();

        await Should.ThrowAsync<NothingToArchiveException>(() => writer.ArchiveAsync(
            repository.Path,
            new ArchiveOptions { Revision = second, OutputPath = file, ChangedSince = second },
            Ct));

        Directory.GetFiles(_output).ShouldBeEmpty();
    }

    [Fact]
    public async Task Basarisiz_arsiv_yarim_dosya_birakmiyor()
    {
        (TestRepository repository, _, string second) = TwoRevisions();
        using TestRepository owned = repository;
        string file = Path.Combine(_output, "broken.zip");

        ArchiveWriter writer = await CreateAsync();

        await Should.ThrowAsync<GitException>(() => writer.ArchiveAsync(
            repository.Path,
            new ArchiveOptions { Revision = second, OutputPath = file, Paths = ["nope"] },
            Ct));

        Directory.GetFiles(_output).ShouldBeEmpty();
    }

    [Fact]
    public async Task Var_olan_dosyanin_ustune_yaziyor()
    {
        (TestRepository repository, _, string second) = TwoRevisions();
        using TestRepository owned = repository;
        string file = Path.Combine(_output, "again.zip");
        await File.WriteAllTextAsync(file, "old", Ct);

        await (await CreateAsync()).ArchiveAsync(
            repository.Path,
            new ArchiveOptions { Revision = second, OutputPath = file, Paths = ["keep.txt"] },
            Ct);

        ZipEntries(file).ShouldBe(["keep.txt"]);
    }
}
