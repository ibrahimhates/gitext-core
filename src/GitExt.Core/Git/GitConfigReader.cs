namespace GitExt.Core.Git;

/// <summary>
/// Etkin <c>git config</c> değerlerini okur (P05-T13).
/// </summary>
/// <remarks>
/// "Etkin" = sistem + global + yerel birleşimi, yani kullanıcının o depoda gerçekten geçerli
/// olan ayarı. <c>git config --get</c> zaten bu birleşimi veriyor; ayrı ayrı dosya okumak
/// öncelik sırasını <b>bizim</b> yeniden uygulamamız demek olurdu.
/// </remarks>
public interface IGitConfigReader
{
    /// <summary>
    /// Bir ayarın ham değerini okur; ayar yoksa veya boşsa <see langword="null"/>.
    /// </summary>
    Task<string?> GetAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir <b>yol</b> ayarını okur; <c>~</c> ve <c>~kullanıcı</c> genişletilir.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ÖLÇÜLDÜ (P05-T13):</b> düz <c>--get</c>, <c>commit.template</c> için
    /// <c>~/.git_commit_msg.txt</c> değerini <b>ham</b> döndürüyor; <c>--path</c> ise aynı
    /// değeri <c>/home/…/.git_commit_msg.txt</c> yapıyor. Ham değeri dosya adı sanmak,
    /// <c>~</c> ile başlayan şablonu <b>sessizce "bulunamadı"</b> yapardı.
    /// </remarks>
    Task<string?> GetPathAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IGitConfigReader"/>
public sealed class GitConfigReader : IGitConfigReader
{
    private readonly IGitProcessRunner _runner;

    public GitConfigReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public Task<string?> GetAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken = default) =>
        ReadAsync(workingDirectory, key, asPath: false, cancellationToken);

    public Task<string?> GetPathAsync(
        string workingDirectory,
        string key,
        CancellationToken cancellationToken = default) =>
        ReadAsync(workingDirectory, key, asPath: true, cancellationToken);

    private async Task<string?> ReadAsync(
        string workingDirectory,
        string key,
        bool asPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        List<string> arguments = ["config"];

        if (asPath)
        {
            arguments.Add("--path");
        }

        // ⚠️ `--get` bilinçli: aynı anahtar birden çok kez tanımlıysa git'in kendi kuralı
        // "son yazan kazanır" ve `--get` tam olarak onu veriyor (ölçüldü: `--get-all` iki
        // satır verirken `--get` sonuncuyu). İlk satırı almak sessizce yanlış olurdu.
        arguments.Add("--get");
        arguments.Add(key);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = arguments,

                // ÖLÇÜLDÜ: ayar tanımlı değilse çıkış kodu 1 ve çıktı boş. Bu bir hata değil,
                // "yok" cevabıdır; hata sayılsaydı yapılandırılmamış her depo istisna atardı.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string value = result.GetStandardOutputText().Trim('\n', '\r');

        // Boş dizeye ayarlı olmak (`git config commit.template ""` → çıkış 0, boş çıktı)
        // "ayarlanmamış" ile aynı anlama gelir; çağıranların bu ayrımı yapması gerekmiyor.
        return value.Length == 0 ? null : value;
    }
}
