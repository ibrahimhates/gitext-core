using System.Collections.Concurrent;
using System.Text;
using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>Kutuya yüklenen mesajın nereden geldiği (P05-T13).</summary>
public enum CommitMessageSource
{
    /// <summary>Yüklenecek bir şey yok.</summary>
    None,

    /// <summary>Kullanıcının yarım bıraktığı taslak.</summary>
    Draft,

    /// <summary>
    /// git'in hazırladığı mesaj (<c>.git/MERGE_MSG</c>): merge, cherry-pick, revert.
    /// </summary>
    Pending,
}

/// <summary>Kutuya yüklenecek mesaj ve kaynağı.</summary>
public sealed record PendingCommitMessage(string Text, CommitMessageSource Source)
{
    public static PendingCommitMessage None { get; } = new(string.Empty, CommitMessageSource.None);

    public bool HasText => Text.Length > 0;
}

/// <summary>
/// Yarım kalmış commit mesajını uygulama kapansa bile saklar (P05-T13).
/// </summary>
/// <remarks>
/// <para>
/// Taslak <b>depo dizininde</b> tutuluyor (<c>.git/GITEXT_COMMITMESSAGE</c>), uygulamanın
/// ayar dosyasında değil: mesaj o deponun o an yapılmakta olan işine ait. Ayarlarda tutmak,
/// depo silindiğinde arkada yetim bir metin bırakır ve iki worktree'yi birbirine karıştırırdı.
/// </para>
/// <para>
/// ⚠️ <b>Git dizini, ortak dizin DEĞİL</b> (P02-T06'daki ayrım): <c>MERGE_MSG</c> ve index
/// worktree başına ayrı, ref'ler ve config ortak. Taslağı ortak dizine koymak, iki worktree'de
/// aynı anda çalışan kullanıcının mesajlarını birbirine karıştırırdı.
/// </para>
/// <para>
/// <c>COMMIT_EDITMSG</c> <b>kullanılmıyor</b>: git onu her commit'te kendi üzerine yazıyor
/// (ölçüldü), yani oraya yazılan taslak sessizce kaybolurdu. GitExtensions da aynı sebeple
/// kendi dosyasını (<c>COMMITMESSAGE</c>) kullanıyor.
/// </para>
/// </remarks>
public interface ICommitMessageStore
{
    /// <summary>
    /// Kutuya yüklenecek mesajı okur: önce git'in hazırladığı, sonra taslak.
    /// </summary>
    Task<PendingCommitMessage> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>Taslağı diske yazar; metin boşsa taslak silinir.</summary>
    Task SaveDraftAsync(
        string workingDirectory,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>Taslağı siler (başarılı commit'ten sonra).</summary>
    Task ClearDraftAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ICommitMessageStore"/>
public sealed class CommitMessageStore : ICommitMessageStore
{
    /// <summary>Taslak dosyasının adı.</summary>
    /// <remarks>
    /// Ön ek bilinçli: <c>.git</c> içindeki dosyalar git'in ad alanı, bizim dosyamızın
    /// kimin olduğu adından anlaşılmalı. Ölçüldü — <c>.git</c> altındaki yabancı bir dosya
    /// <c>git status</c> ve <c>git fsck</c> çıktısını etkilemiyor.
    /// </remarks>
    public const string DraftFileName = "GITEXT_COMMITMESSAGE";

    /// <summary>git'in merge/cherry-pick/revert için hazırladığı mesaj dosyası.</summary>
    public const string PendingFileName = "MERGE_MSG";

    private readonly IGitProcessRunner _runner;
    private readonly IGitConfigReader _config;

    private readonly ConcurrentDictionary<string, string> _gitDirectories = new(StringComparer.Ordinal);

    public CommitMessageStore(IGitProcessRunner runner, IGitConfigReader config)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(config);

        _runner = runner;
        _config = config;
    }

    public async Task<PendingCommitMessage> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string? gitDirectory = await ResolveGitDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (gitDirectory is null)
        {
            return PendingCommitMessage.None;
        }

        // Sıra önemli: git bir merge'in ortasındaysa onun hazırladığı mesaj kazanır.
        // ÖLÇÜLDÜ: MERGE_MSG yalnızca çakışmada değil, çakışmasız `--no-ff` merge'de de
        // yazılıyor ve cherry-pick çakışmasında da (commit'in kendi mesajıyla) oluşuyor;
        // git commit başarılı olunca git onu KENDİSİ siliyor.
        string pendingPath = Path.Combine(gitDirectory, PendingFileName);

        if (File.Exists(pendingPath))
        {
            string? pending = await ReadPendingAsync(workingDirectory, pendingPath, cancellationToken)
                .ConfigureAwait(false);

            if (pending is { Length: > 0 })
            {
                return new PendingCommitMessage(pending, CommitMessageSource.Pending);
            }
        }

        string draftPath = Path.Combine(gitDirectory, DraftFileName);

        if (!File.Exists(draftPath))
        {
            return PendingCommitMessage.None;
        }

        try
        {
            // Taslak BİZİM dosyamız ve her zaman UTF-8 yazılıyor; burada tahmin yok.
            // Yorum satırları da temizlenmiyor: kullanıcının kendi yazdığı `#123` satırı
            // onun metnidir (P05-T06'da `--cleanup=whitespace` seçilme gerekçesiyle aynı).
            string draft = await File.ReadAllTextAsync(draftPath, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            return draft.Trim().Length == 0
                ? PendingCommitMessage.None
                : new PendingCommitMessage(draft, CommitMessageSource.Draft);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Taslağın okunamaması commit ekranını açılmaz yapmamalı.
            return PendingCommitMessage.None;
        }
    }

    public async Task SaveDraftAsync(
        string workingDirectory,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(message);

        if (message.Trim().Length == 0)
        {
            await ClearDraftAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            return;
        }

        string? gitDirectory = await ResolveGitDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (gitDirectory is null || !Directory.Exists(gitDirectory))
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(
                    Path.Combine(gitDirectory, DraftFileName),
                    message,
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Salt okunur bir depoda taslak kaydedilemez; bu, kullanıcıya gösterilecek bir
            // hata değil — mesaj kutusu ekranda duruyor ve commit hâlâ atılabiliyor.
        }
    }

    public async Task ClearDraftAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string? gitDirectory = await ResolveGitDirectoryAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (gitDirectory is null)
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(gitDirectory, DraftFileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// git'in hazırladığı mesajı okur ve yorum satırlarını temizler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Yorumlar burada siliniyor</b> (bkz. <see cref="CommitMessageText"/>): git'in
    /// editör yolu <c># Conflicts:</c> satırlarını commit'e sokmuyor, bizim
    /// <c>--cleanup=whitespace</c> yolumuz ise sokardı.
    /// </para>
    /// <para>
    /// <b>ÖLÇÜLDÜ — kodlama:</b> git bu dosyayı <b>ham baytlarla</b> yazıyor;
    /// <c>i18n.commitEncoding=ISO-8859-9</c> olan bir depoda cherry-pick edilen commit'in
    /// mesajı dosyaya Latin-5 baytlarıyla düştü. UTF-8 varsayılsaydı Türkçe mesajlar
    /// değiştirme karakterine dönerdi (P04-T07'deki diff kodlama hatasının aynısı).
    /// </para>
    /// </remarks>
    private async Task<string?> ReadPendingAsync(
        string workingDirectory,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

            string? commitEncoding = await _config
                .GetAsync(workingDirectory, "i18n.commitEncoding", cancellationToken)
                .ConfigureAwait(false);

            Encoding encoding = TextEncodings.TryGet(commitEncoding) ?? TextEncodings.Default;

            string? commentCharacter = await _config
                .GetAsync(workingDirectory, "core.commentChar", cancellationToken)
                .ConfigureAwait(false);

            return CommitMessageText.PrepareForEditing(encoding.GetString(bytes), commentCharacter);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deponun git dizinini çözer ve depo başına önbelleğe alır.
    /// </summary>
    /// <remarks>
    /// <c>rev-parse --git-path</c> <b>kullanılmıyor</b>: ölçüldü, normal depoda
    /// <c>.git/MERGE_MSG</c> gibi <b>göreli</b> bir yol döndürüyor ve bu yol komutun
    /// çalıştığı dizine göre çözülüyor (<c>--git-common-dir</c>'deki P02-T06 tuzağının
    /// aynısı). <c>--absolute-git-dir</c> her durumda mutlak; worktree'de de doğru olanı,
    /// yani o worktree'nin kendi dizinini veriyor.
    /// </remarks>
    private async Task<string?> ResolveGitDirectoryAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (_gitDirectories.TryGetValue(workingDirectory, out string? cached))
        {
            return cached;
        }

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--absolute-git-dir"],
                SuccessExitCodes = [0, 128],
            },
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        string directory = result.GetStandardOutputText().Trim('\n', '\r');

        if (directory.Length == 0)
        {
            return null;
        }

        _gitDirectories[workingDirectory] = directory;

        return directory;
    }
}
