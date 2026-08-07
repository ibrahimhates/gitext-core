namespace GitExt.Core.Git;

/// <summary>
/// Bir <c>git config</c> ayarının hangi dosyaya yazılacağı (P08-T15).
/// </summary>
public enum GitConfigScope
{
    /// <summary>Yalnızca bu depo (<c>.git/config</c>).</summary>
    Local,

    /// <summary>Kullanıcının bütün depoları (<c>~/.gitconfig</c>).</summary>
    Global,
}

/// <summary>
/// <c>git config</c> ayarlarını yazar (P08-T15).
/// </summary>
public interface IGitConfigWriter
{
    /// <summary>
    /// Belirli bir kapsamdaki <b>ham</b> değeri okur (birleşimi değil).
    /// </summary>
    /// <remarks>
    /// Ayarlar ekranının "yerel" ve "global" alanlarını doldurmak için gerekli:
    /// <see cref="IGitConfigReader"/> birleşimi veriyor ve o birleşim, değerin hangi
    /// dosyadan geldiğini <b>söylemiyor</b>. Kullanıcıya global bir değeri yerel alanda
    /// göstermek, kaydettiğinde farkında olmadan yerel bir kopya oluşturması demekti.
    /// </remarks>
    Task<string?> GetScopedAsync(
        string workingDirectory,
        string key,
        GitConfigScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ayarı yazar; <paramref name="value"/> boşsa ayarı <b>kaldırır</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Boş değer "sil" demek, "boşa ayarla" değil.</b> Ölçüldü:
    /// <c>git config user.name ""</c> çıkış kodu 0 veriyor ve ayar <b>var ama boş</b>
    /// oluyor — <c>--get</c> onu çıkış kodu 0 ve boş çıktıyla döndürüyor. Boş bir
    /// <c>user.name</c> ile commit atmak, hiç ayarlanmamış olmasından farklı ve daha kötü
    /// bir hata üretir. Kullanıcı alanı temizlediğinde kastettiği "sil"dir.
    /// </remarks>
    Task SetAsync(
        string workingDirectory,
        string key,
        string value,
        GitConfigScope scope,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IGitConfigWriter"/>
public sealed class GitConfigWriter : IGitConfigWriter
{
    /// <summary>
    /// <c>--unset</c>'in "böyle bir anahtar yok" çıkış kodu.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>ÖLÇÜLDÜ:</b> 0 ya da 1 değil, <b>5</b>. Hata sayılsaydı zaten boş olan bir alanı
    /// temizlemek kullanıcıya hata gösterirdi — hem de hiçbir şey yanlış gitmemişken.
    /// </remarks>
    private const int UnsetMissingKeyExitCode = 5;

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public GitConfigWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<string?> GetScopedAsync(
        string workingDirectory,
        string key,
        GitConfigScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", ScopeFlag(scope), "--get", key],

                // 🔴 ÖLÇÜLDÜ. 1 = "ayarlanmamış" — global dosya hiç yokken de 1, hata değil.
                // 128 = depo dışında `--local` (`fatal: --local can only be used inside a
                // git repository`); ekran bunu sunmuyor ama komut satırından verilen bir
                // dizin depo olmayabilir ve bunun için çökmemeliyiz.
                SuccessExitCodes = [0, 1, 128],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string value = result.GetStandardOutputText().Trim('\n', '\r');

        // Burada boş dize `null`'a ÇEVRİLMİYOR: "var ama boş" gerçek bir durum ve ekranın
        // onu gösterip düzeltebilmesi gerekiyor.
        return value;
    }

    public Task SetAsync(
        string workingDirectory,
        string key,
        string value,
        GitConfigScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return string.IsNullOrEmpty(value)
            ? UnsetAsync(workingDirectory, key, scope, cancellationToken)
            : _writer.RunAsync(
                workingDirectory,
                ["config", ScopeFlag(scope), key, value],
                cancellationToken);
    }

    private async Task UnsetAsync(
        string workingDirectory,
        string key,
        GitConfigScope scope,
        CancellationToken cancellationToken)
    {
        try
        {
            await _writer
                .RunAsync(workingDirectory, ["config", ScopeFlag(scope), "--unset", key], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GitException ex) when (ex.ExitCode == UnsetMissingKeyExitCode)
        {
            // Anahtar zaten yok. İstenen son durum sağlanmış durumda; bunu hata olarak
            // yukarı taşımak, boş bir alanı temizleyen kullanıcıya sebepsiz hata gösterirdi.
        }
    }

    private static string ScopeFlag(GitConfigScope scope) =>
        scope == GitConfigScope.Global ? "--global" : "--local";
}
