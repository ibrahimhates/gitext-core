using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Commit oluşturma seçenekleri (P05-T06).
/// </summary>
public sealed record CommitOptions
{
    public static CommitOptions Default { get; } = new();

    /// <summary>Son commit'in üzerine yaz (<c>--amend</c>).</summary>
    /// <remarks>
    /// ⚠️ Yayınlanmış bir commit'te geçmişi yeniden yazar. Arayüz bunu kullanıcıya
    /// bildirmeli (P05-T15).
    /// </remarks>
    public bool Amend { get; init; }

    /// <summary>Mesajın sonuna <c>Signed-off-by</c> satırı ekle.</summary>
    public bool SignOff { get; init; }

    /// <summary>Değişiklik olmasa da commit oluştur (<c>--allow-empty</c>).</summary>
    public bool AllowEmpty { get; init; }

    /// <summary>Boş mesaja izin ver (<c>--allow-empty-message</c>).</summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> boş mesajla <c>git commit</c> çıkış <b>1</b> veriyor
    /// (<i>Aborting commit due to empty commit message</i>). Bu bayrak olmadan mesajsız
    /// commit oluşturulamaz.
    /// </remarks>
    public bool AllowEmptyMessage { get; init; }

    /// <summary>
    /// Doğrulama hook'larını atla (<c>--no-verify</c>). <b>Varsayılan kapalı.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ÖLÇÜLDÜ:</b> başarısız bir <c>pre-commit</c> hook'u commit'i çıkış 1 ile
    /// durduruyor ve çıktısını <c>stderr</c>'e yazıyor. Bu, kullanıcının kurduğu doğrulamayı
    /// devre dışı bırakmak demek — arayüz açıkken görünür bir uyarı göstermeli (plan gereği).
    /// </para>
    /// <para>
    /// ⚠️ <b>"Hook'ları atla" DEĞİL.</b> Ölçüldü (P05-T07): <c>--no-verify</c> yalnızca
    /// <c>pre-commit</c> ve <c>commit-msg</c>'i atlıyor; <c>prepare-commit-msg</c> ve
    /// <c>post-commit</c> <b>yine çalışıyor</b>. Yani bu bayrak açıkken bile mesaj
    /// değişebilir (<see cref="CommitResult.MessageChanged"/>) ve çıktı gelebilir.
    /// </para>
    /// </remarks>
    public bool SkipHooks { get; init; }

    /// <summary>Yazarı değiştir; biçim <c>Ad Soyad &lt;eposta&gt;</c>.</summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> yalnızca <i>author</i> alanını değiştiriyor; <i>committer</i>
    /// kullanıcının kendi kimliği olarak kalıyor — git'in doğru davranışı budur.
    /// </remarks>
    public string? Author { get; init; }

    /// <summary>Commit'i GPG/SSH ile imzala (<c>-S</c>).</summary>
    public bool Sign { get; init; }

    /// <summary>İmzalamada kullanılacak anahtar; boşsa git'in yapılandırması geçerli.</summary>
    public string? SigningKey { get; init; }
}

/// <summary>
/// Tamamlanmış bir commit'in sonucu (P05-T07).
/// </summary>
/// <remarks>
/// Yalnızca <see cref="CommitId"/> döndürmek iki bilgiyi <b>sessizce</b> yutuyordu:
/// hook'ların yazdıkları ve hook'ların mesajda yaptığı değişiklik.
/// </remarks>
public sealed record CommitResult
{
    /// <summary>Oluşan commit'in kimliği.</summary>
    public required CommitId Id { get; init; }

    /// <summary>Commit'e gerçekten giren mesaj (<c>%B</c> ile geri okundu).</summary>
    public required string Message { get; init; }

    /// <summary>Çağıranın verdiği mesaj.</summary>
    public required string RequestedMessage { get; init; }

    /// <summary>
    /// <c>git commit</c>'in tanı çıktısı — <b>hook çıktısı dahil</b>.
    /// </summary>
    /// <remarks>
    /// Ham metin. Gösterilmeden önce <see cref="GitOutputText.CleanForDisplay"/> ile
    /// geçirilmeli (ANSI kodları ve <c>\r</c> ilerleme satırları geliyor).
    /// <para>
    /// <b>ÖLÇÜLDÜ:</b> commit <b>başarılı</b> olsa bile hook'lar buraya yazıyor — başarılı bir
    /// <c>pre-commit</c>'in uyarıları, <c>post-commit</c>'in çıktısı. Eskiden bu sonuç hiç
    /// döndürülmediği için hepsi kayboluyordu.
    /// </para>
    /// </remarks>
    public required string Output { get; init; }

    /// <summary>Gösterilecek bir çıktı var mı?</summary>
    public bool HasOutput => Output.Length > 0;

    /// <summary>
    /// Kullanıcıya anlatılacak bir şey var mı? (çıktı ya da değişmiş mesaj)
    /// </summary>
    /// <remarks>
    /// Hook'suz bir depoda her commit'ten sonra boş bir pencere açmak, kullanıcının kapatmayı
    /// öğrendiği ve sonra gerçekten önemli olanı da kapattığı bir gürültü olurdu. Ölçüldü:
    /// hook'suz başarılı commit'te çıktı <b>tamamen boş</b>, yani bu ayrım pratikte çalışıyor.
    /// </remarks>
    public bool NeedsReporting => HasOutput || MessageChanged;

    /// <summary>
    /// Commit'e giren mesaj, istenen mesajdan farklı mı?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ÖLÇÜLDÜ:</b> <c>prepare-commit-msg</c> ve <c>commit-msg</c> hook'ları mesaj dosyasını
    /// yerinde düzenleyebiliyor (<c>Change-Id</c> eklemek en yaygın örnek) ve sonuç doğrudan
    /// commit'e giriyor. Kullanıcı yazdığından farklı bir mesajın kaydedildiğini
    /// <b>görmeli</b>.
    /// </para>
    /// <para>
    /// ⚠️ Fark yalnız hook'lardan gelmeyebilir: <c>--signoff</c> de mesaja satır ekliyor.
    /// Bu yüzden ad "hook değiştirdi" değil "mesaj değişti" — sebebi iddia etmiyoruz.
    /// Yalnızca <c>--cleanup=whitespace</c>'in kendi normalleştirmesi (satır sonu boşlukları,
    /// baştaki/sondaki boş satırlar) fark sayılmaz; o bizim istediğimiz davranış.
    /// </para>
    /// </remarks>
    public bool MessageChanged =>
        !string.Equals(Normalize(Message), Normalize(RequestedMessage), StringComparison.Ordinal);

    /// <summary>
    /// <c>--cleanup=whitespace</c>'in yaptığı normalleştirmenin aynısı: her satırın sonundaki
    /// boşluk ve baştaki/sondaki boş satırlar atılır.
    /// </summary>
    private static string Normalize(string message) =>
        string.Join(
                '\n',
                message
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Split('\n')
                    .Select(line => line.TrimEnd()))
            .Trim('\n');
}

/// <summary>
/// Commit oluşturur (P05-T06).
/// </summary>
public interface ICommitWriter
{
    /// <summary>
    /// Index'teki değişikliklerden bir commit oluşturur ve yeni commit'in kimliğini döndürür.
    /// </summary>
    /// <param name="workingDirectory">Depo çalışma dizini.</param>
    /// <param name="message">Commit mesajı; <b>stdin</b> ile geçirilir.</param>
    /// <param name="options">Seçenekler; <see langword="null"/> ise varsayılanlar.</param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    Task<CommitResult> CommitAsync(
        string workingDirectory,
        string message,
        CommitOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICommitWriter"/>
public sealed class CommitWriter : ICommitWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public CommitWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<CommitResult> CommitAsync(
        string workingDirectory,
        string message,
        CommitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(message);

        options ??= CommitOptions.Default;

        // `-F -`: mesaj stdin'den. Argüman olarak geçirmek uzunluk sınırına takılır ve
        // kullanıcı metnini kabuk yorumlamasına açardı (ADR-0002).
        List<string> arguments = ["commit", "-F", "-"];

        // ⚠️ `--cleanup=whitespace` AÇIKÇA veriliyor: kullanıcının `commit.cleanup` ayarı
        // davranışı değiştirebilir ve mesajı beklenmedik biçimde kırpabilirdi. Ölçüldü:
        // bu modda `#` ile başlayan satırlar KORUNUYOR (issue referansları kaybolmuyor),
        // yalnızca baştaki/sondaki fazla boşluk temizleniyor.
        arguments.Add("--cleanup=whitespace");

        if (options.Amend)
        {
            arguments.Add("--amend");
        }

        if (options.SignOff)
        {
            arguments.Add("--signoff");
        }

        if (options.AllowEmpty)
        {
            arguments.Add("--allow-empty");
        }

        if (options.AllowEmptyMessage)
        {
            arguments.Add("--allow-empty-message");
        }

        if (options.SkipHooks)
        {
            arguments.Add("--no-verify");
        }


        if (options.Author is { Length: > 0 } author)
        {
            arguments.Add($"--author={author}");
        }

        if (options.Sign)
        {
            arguments.Add(options.SigningKey is { Length: > 0 } key ? $"-S{key}" : "-S");
        }

        // Süreç sınırı burada verilmiyor: hook'ların keyfi uzun sürebilmesi tek bir komutun
        // değil YAZMA YOLUNUN özelliği (bkz. GitWriter.DefaultWriteTimeout).
        GitResult result = await _writer.RunAsync(
                workingDirectory, arguments, message, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        (CommitId id, string storedMessage) =
            await ReadHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false);

        return new CommitResult
        {
            Id = id,
            Message = storedMessage,
            RequestedMessage = message,

            // ⚠️ Komut BAŞARILI olsa da çıktı taşınıyor: hook'lar başarı yolunda da yazıyor
            // (uyarılar, `post-commit`). Bunu atmak, kullanıcının kurduğu doğrulamanın
            // söylediklerini yutmak olurdu — ADR-0002'de CLI'ın seçilme gerekçesi buydu.
            Output = result.StandardError,
        };
    }

    /// <summary>
    /// Yeni commit'in kimliğini <b>ve</b> kaydedilen mesajını okur.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>git commit</c> çıktısı <b>insan-okunur</b> (<c>[main ec6c0d6] konu</c>) ve
    /// ayrıştırılmaz (proje kuralı). Kimlik ayrı bir okumayla alınıyor.
    /// </para>
    /// <para>
    /// Mesaj da <b>geri okunuyor</b>: <c>prepare-commit-msg</c> ve <c>commit-msg</c> hook'ları
    /// mesajı değiştirebiliyor, dolayısıyla gönderdiğimiz metin commit'e girenle aynı
    /// olmayabilir. İkisi tek çağrıda alınıyor — ayraç <c>%x00</c>, çünkü commit mesajı NUL
    /// baytı <b>içeremez</b> (P02-T04'te ölçüldü, git reddediyor).
    /// </para>
    /// </remarks>
    private async Task<(CommitId Id, string Message)> ReadHeadAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["log", "-1", "--format=%H%x00%B"],
            },
            cancellationToken).ConfigureAwait(false);

        string[] fields = result.SplitStandardOutputAtNulPreservingEmpty();

        if (fields.Length < 2)
        {
            throw new GitException(
                GitFailureKind.Unknown,
                "The commit was created but its id could not be read.",
                "git log -1 --format=%H%x00%B",
                result.ExitCode,
                result.StandardError);
        }

        // `git log` biçimin sonuna kendi satır sonunu ekliyor; mesajın kendi sonu da var.
        return (CommitId.Parse(fields[0].Trim()), fields[1].TrimEnd('\n'));
    }
}
