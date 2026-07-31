using System.Text.RegularExpressions;
using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// <c>commit.template</c> ayarının sonucu (P05-T13).
/// </summary>
/// <remarks>
/// Ayar var ama dosya yok durumu <b>sessizce yutulmuyor</b>: git'in kendisi bu durumda
/// <c>fatal: could not read '…': No such file or directory</c> ile <b>çıkış 128</b> veriyor
/// (ölçüldü), yani kullanıcının terminaldeki commit'i de çalışmıyor. Ekranda "şablon boş"
/// göstermek, bozuk yapılandırmayı gizlemek olurdu.
/// </remarks>
public sealed record CommitTemplate
{
    /// <summary>Çözülmüş tam yol.</summary>
    public required string Path { get; init; }

    /// <summary>Şablon metni; dosya okunamadıysa <see langword="null"/>.</summary>
    public string? Text { get; init; }

    /// <summary>Dosya bulunamadı ya da okunamadı.</summary>
    public bool IsMissing => Text is null;
}

/// <summary>
/// Commit mesajı kaynaklarını okur: geçmiş, <c>HEAD</c> mesajı, şablon (P05-T13).
/// </summary>
public interface ICommitMessageReader
{
    /// <summary>
    /// Son commit mesajlarını en yeniden eskiye döndürür.
    /// </summary>
    /// <param name="workingDirectory">Depo çalışma dizini.</param>
    /// <param name="count">En fazla kaç mesaj.</param>
    /// <param name="onlyCurrentUser">
    /// Yalnızca yapılandırılmış kullanıcının (<c>user.name</c>/<c>user.email</c>) commit'leri.
    /// </param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    Task<IReadOnlyList<string>> ReadRecentAsync(
        string workingDirectory,
        int count,
        bool onlyCurrentUser = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>HEAD</c>'in mesajı; commit'siz depoda <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <c>--amend</c> kutusu işaretlendiğinde yüklenen metin budur.
    /// </remarks>
    Task<string?> ReadHeadMessageAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>commit.template</c> ile yapılandırılmış şablon; ayar yoksa <see langword="null"/>.
    /// </summary>
    Task<CommitTemplate?> ReadTemplateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bu depoda geçerli yorum ön eki (<c>core.commentChar</c>).
    /// </summary>
    Task<string> ReadCommentCharacterAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICommitMessageReader"/>
public sealed class CommitMessageReader : ICommitMessageReader
{
    private readonly IGitProcessRunner _runner;
    private readonly IGitConfigReader _config;

    public CommitMessageReader(IGitProcessRunner runner, IGitConfigReader config)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(config);

        _runner = runner;
        _config = config;
    }

    public async Task<IReadOnlyList<string>> ReadRecentAsync(
        string workingDirectory,
        int count,
        bool onlyCurrentUser = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        // Commit'siz depoda `git log` çıkış 128 veriyor (ölçüldü). "Henüz commit yok" bir
        // hata değil; ilk commit'ini atan kullanıcıya istisna göstermek olurdu.
        if (!await HasHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        // ⚠️ `-z` ŞART: `%B` çok satırlı ve mesajlar arasında ayraç olarak satır sonu
        // kullanılsaydı bir mesajın nerede bittiği belirlenemezdi (ölçüldü: `-z` olmadan
        // çıktı düz bir satır yığını). `-z` her kaydın sonuna NUL koyuyor, commit mesajı
        // ise NUL içeremiyor (P02-T04).
        List<string> arguments = ["log", "-z", "-n", count.ToString(), "--format=%B"];

        if (onlyCurrentUser)
        {
            string? pattern = await BuildAuthorPatternAsync(workingDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (pattern is not null)
            {
                arguments.Add($"--author={pattern}");
            }
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand { WorkingDirectory = workingDirectory, Arguments = arguments },
            cancellationToken).ConfigureAwait(false);

        // NUL bir SONLANDIRICI: n kayıt için n NUL geliyor, son alan boş kalıyor. Boş alanları
        // koruyan bölme kullanılıyor (proje kuralı) — boş mesajlı commit'ler gerçek
        // (P02-T04'te ölçüldü) ve "boşları at" diyen bir bölme onları sessizce yutar.
        string[] records = result.SplitStandardOutputAtNulPreservingEmpty();

        return
        [
            .. records
                .Select(record => record.TrimEnd('\n', '\r'))

                // Boş mesajlar listede gösterilmiyor: seçilecek bir şey değiller.
                // Ama bu bir GÖSTERİM kararı; bölme aşamasında atılsalardı sıra bozulurdu.
                .Where(message => message.Trim().Length > 0),
        ];
    }

    public async Task<string?> ReadHeadMessageAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (!await HasHeadAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["log", "-1", "--format=%B"],
            },
            cancellationToken).ConfigureAwait(false);

        return result.GetStandardOutputText().TrimEnd('\n', '\r');
    }

    public async Task<CommitTemplate?> ReadTemplateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        // `--path` olmadan `~/…` ham geliyor ve dosya hiç bulunamıyordu (ölçüldü).
        string? configured = await _config
            .GetPathAsync(workingDirectory, "commit.template", cancellationToken)
            .ConfigureAwait(false);

        if (configured is null)
        {
            return null;
        }

        string path = await ResolveTemplatePathAsync(workingDirectory, configured, cancellationToken)
            .ConfigureAwait(false);

        string? text = null;

        try
        {
            if (File.Exists(path))
            {
                // Şablon dosyası kullanıcının kendi dosyası; kodlaması bilinmiyor. UTF-8
                // varsayılıyor, geçersiz baytlar değiştirme karakterine düşüyor — yamalarda
                // (P04-T07) yapılan gibi burada tahmin edilmiyor, ama şablon commit'e
                // gitmeden önce kullanıcının GÖZÜNÜN ÖNÜNDE kutuda duruyor.
                text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Okunamayan şablon = bulunamayan şablon: ikisinde de kullanıcıya yol gösteriliyor.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new CommitTemplate { Path = path, Text = text };
    }

    public async Task<string> ReadCommentCharacterAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        string? configured = await _config
            .GetAsync(workingDirectory, "core.commentChar", cancellationToken)
            .ConfigureAwait(false);

        return CommitMessageText.ResolveCommentCharacter(configured);
    }

    /// <summary>
    /// Göreli şablon yolunu çözer.
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> git göreli <c>commit.template</c> yolunu <b>çalışma ağacının köküne</b>
    /// göre çözüyor, komutun çalıştığı dizine göre değil — alt dizinde aynı adlı bir dosya
    /// varken bile kökteki okundu, yalnızca alt dizinde olan dosya ise
    /// <c>could not read</c> ile bulunamadı. Bu yüzden kök ayrıca soruluyor: çağıran bir alt
    /// dizin verirse git'ten farklı bir dosya açmak, kullanıcıya terminalde gördüğünden
    /// başka bir şablon göstermek olurdu.
    /// </remarks>
    private async Task<string> ResolveTemplatePathAsync(
        string workingDirectory,
        string configured,
        CancellationToken cancellationToken)
    {
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--show-toplevel"],

                // Bare depoda 128 veriyor (P02-T06'da ölçüldü). Orada commit ekranı zaten
                // yok; çağıranın dizini yedek olarak kullanılıyor.
                SuccessExitCodes = [0, 128],
            },
            cancellationToken).ConfigureAwait(false);

        string root = result.ExitCode == 0
            ? result.GetStandardOutputText().Trim('\n', '\r')
            : string.Empty;

        return Path.GetFullPath(
            Path.Combine(root.Length > 0 ? root : workingDirectory, configured));
    }

    /// <summary>
    /// "Yalnızca benim mesajlarım" için <c>--author</c> deseni.
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> <c>--author</c> düz alt dize değil <b>düzenli ifade</b> olarak
    /// eşleşiyor (<c>lcum</c> deseni <c>Ölçüm</c>'ü buluyor, <c>^…$</c> çapaları çalışıyor).
    /// Ad ve e-posta bu yüzden hem kaçırılıyor hem çapalanıyor: aksi hâlde adı başka bir adın
    /// içinde geçen herkesin commit'i "benim" sayılırdı.
    /// </remarks>
    private async Task<string?> BuildAuthorPatternAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        string? name = await _config.GetAsync(workingDirectory, "user.name", cancellationToken)
            .ConfigureAwait(false);

        string? email = await _config.GetAsync(workingDirectory, "user.email", cancellationToken)
            .ConfigureAwait(false);

        if (name is null && email is null)
        {
            return null;
        }

        return $"^{Regex.Escape(name ?? string.Empty)} <{Regex.Escape(email ?? string.Empty)}>$";
    }

    /// <summary>
    /// Depoda commit var mı?
    /// </summary>
    /// <remarks>
    /// Mesaja değil <c>rev-parse --verify --quiet</c>'e bakılıyor (P05-T03'teki karar):
    /// git'in hata metni yerelleştirilebilir ve sürümle değişebilir.
    /// </remarks>
    private async Task<bool> HasHeadAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", "HEAD"],
                SuccessExitCodes = [0, 1, 128],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }
}
