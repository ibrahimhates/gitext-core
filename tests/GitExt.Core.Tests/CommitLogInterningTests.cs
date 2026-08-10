using GitExt.Core.Git;
using GitExt.Core.Model;
using GitExt.Core.Tests.Fixtures;

namespace GitExt.Core.Tests;

/// <summary>
/// P09-T08 — commit okumada metin havuzu (interning).
/// </summary>
/// <remarks>
/// <para>
/// Ölçüm: 500.000 commit'lik depoda yazar/committer alanları 46 MB tutuyordu ama
/// benzersiz değer sayısı <b>2</b>'ydi. Havuz sonrası tutulan bellek 460 MB → 368 MB.
/// </para>
/// <para>
/// Buradaki testlerin işi kazancı değil, <b>kazancın doğruluğu bozmadığını</b>
/// doğrulamak: paylaşılan bir örnek döndürmek, okunan verinin kendisini değiştirmemeli.
/// </para>
/// </remarks>
public class CommitLogInterningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<CommitLogReader> CreateAsync()
    {
        GitExecutable executable = await GitExecutable.LocateAsync(cancellationToken: Ct)
            .ConfigureAwait(true);

        return new CommitLogReader(new GitProcessRunner(executable));
    }

    private static TestRepository RepositoryWithTwoAuthors()
    {
        TestRepository repo = TestRepository.CreateWithSingleCommit();

        repo.WriteFile("a.txt", "a");
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=Ayşe Yılmaz", "-c", "user.email=ayse@example.com",
            "commit", "-q", "-m", "ilk");

        repo.WriteFile("b.txt", "b");
        repo.Git("add", "-A");
        repo.Git("-c", "user.name=Ayşe Yılmaz", "-c", "user.email=ayse@example.com",
            "commit", "-q", "-m", "ikinci");

        return repo;
    }

    /// <remarks>
    /// Havuzun işi bu: aynı yazar adı iki commit'te de <b>aynı örnek</b> olmalı.
    /// Referans eşitliği kontrol ediliyor çünkü ölçülen kazanç tam olarak bu — değer
    /// eşitliği havuz olmadan da sağlanırdı ve hiçbir şey kanıtlamazdı.
    /// </remarks>
    [Fact]
    public async Task Ayni_yazar_ayni_ORNEGI_paylasiyor()
    {
        using TestRepository repo = RepositoryWithTwoAuthors();
        CommitLogReader reader = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        CommitInfo[] byAyse = [.. commits.Where(c => c.Author.Name == "Ayşe Yılmaz")];
        byAyse.Length.ShouldBeGreaterThanOrEqualTo(2);

        ReferenceEquals(byAyse[0].Author.Name, byAyse[1].Author.Name)
            .ShouldBeTrue("yazar adı iki commit arasında paylaşılmıyor");
        ReferenceEquals(byAyse[0].Author.Email, byAyse[1].Author.Email)
            .ShouldBeTrue("yazar e-postası paylaşılmıyor");
    }

    /// <remarks>
    /// 🔴 Havuz <b>değeri</b> değiştirmemeli. Bir eşleme hatası, iki farklı yazarı tek
    /// isim altında birleştirir; commit listesi sessizce yanlış kişiyi gösterirdi —
    /// bellek kazancının hiçbir şekilde haklı çıkaramayacağı bir hata.
    /// </remarks>
    [Fact]
    public async Task Farkli_yazarlar_birbirine_karismiyor()
    {
        using TestRepository repo = RepositoryWithTwoAuthors();
        CommitLogReader reader = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true);

        string[] names = [.. commits.Select(c => c.Author.Name).Distinct(StringComparer.Ordinal)];

        names.ShouldContain("Ayşe Yılmaz");
        names.Length.ShouldBeGreaterThanOrEqualTo(2, "ikinci yazar kaybolmuş");
    }

    /// <remarks>
    /// Akış yolu ayrı bir kod yolu ve grafik tam olarak oradan besleniyor; havuz orada
    /// da devrede olmalı.
    /// </remarks>
    [Fact]
    public async Task Akis_yolunda_da_paylasim_var()
    {
        using TestRepository repo = RepositoryWithTwoAuthors();
        CommitLogReader reader = await CreateAsync().ConfigureAwait(true);

        List<CommitInfo> commits = [];

        await foreach (CommitInfo commit in reader
            .StreamAsync(repo.Path, new CommitLogQuery { MaxCount = 10 }, Ct)
            .ConfigureAwait(true))
        {
            commits.Add(commit);
        }

        CommitInfo[] byAyse = [.. commits.Where(c => c.Author.Name == "Ayşe Yılmaz")];
        byAyse.Length.ShouldBeGreaterThanOrEqualTo(2);

        ReferenceEquals(byAyse[0].Author.Name, byAyse[1].Author.Name)
            .ShouldBeTrue("akış yolunda yazar adı paylaşılmıyor");
    }

    /// <remarks>
    /// Boş alan (kodlaması olmayan commit) havuza girmemeli; <see cref="string.Empty"/>
    /// zaten tek örnek ve sözlüğe boş anahtar koymak yalnızca gürültü olurdu.
    /// </remarks>
    [Fact]
    public async Task Bos_kodlama_alani_bos_dize_kaliyor()
    {
        using TestRepository repo = TestRepository.CreateWithSingleCommit();
        CommitLogReader reader = await CreateAsync().ConfigureAwait(true);

        IReadOnlyList<CommitInfo> commits = await reader
            .ReadAsync(repo.Path, new CommitLogQuery { MaxCount = 1 }, Ct)
            .ConfigureAwait(true);

        commits[0].Encoding.ShouldBe(string.Empty);
    }
}
