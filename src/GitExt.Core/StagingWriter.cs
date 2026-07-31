using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Dosya seviyesinde stage / unstage işlemleri (P05-T03).
/// </summary>
public interface IStagingWriter
{
    /// <summary>Verilen yolları index'e alır (<c>git add</c>).</summary>
    /// <remarks>Silinmiş dosyalar da alınır: silme işlemi de bir değişikliktir.</remarks>
    Task StageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default);

    /// <summary>Verilen yolları index'ten çıkarır; çalışma ağacına dokunmaz.</summary>
    Task UnstageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Seçilen satırları/hunk'ları index'e taşır — <b>kısmi stage</b> (P05-T04).
    /// </summary>
    /// <param name="workingDirectory">Depo çalışma dizini.</param>
    /// <param name="diff">
    /// Çalışma ağacı ile index arasındaki fark (<c>git diff</c>). Yama bundan üretilir.
    /// </param>
    /// <param name="selection">Uygulanacak satırlar.</param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    /// <param name="contentEncoding">
    /// Dosyanın kodlaması; varsayılan UTF-8. Diff <c>DiffOptions.ContentEncoding</c> ile
    /// okunduysa aynısı verilmelidir.
    /// </param>
    Task StagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Seçilen satırları/hunk'ları index'ten geri alır — <b>kısmi unstage</b> (P05-T04).
    /// </summary>
    /// <param name="workingDirectory">Depo çalışma dizini.</param>
    /// <param name="diff">
    /// Index ile <c>HEAD</c> arasındaki fark (<c>git diff --cached</c>). Yama bundan
    /// üretilip <b>ters</b> uygulanır.
    /// </param>
    /// <param name="selection">Geri alınacak satırlar.</param>
    /// <param name="cancellationToken">İptal jetonu.</param>
    /// <param name="contentEncoding">Dosyanın kodlaması; varsayılan UTF-8.</param>
    Task UnstagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dosyayı takipten çıkarır ama <b>diskte bırakır</b> (<c>git rm --cached</c>).
    /// </summary>
    /// <remarks>
    /// Bu <b>unstage değildir</b>: takip edilen bir dosyada sonuç, dosyanın <i>silinmiş</i>
    /// olarak stage'lenmesidir. Ayrı bir komut olarak duruyor çünkü kullanıcının bilinçli
    /// olarak isteyebileceği bir işlem (ör. yanlışlıkla eklenmiş bir yapılandırma dosyasını
    /// depodan çıkarmak).
    /// </remarks>
    Task UntrackAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IStagingWriter"/>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ (P05-T03) — unstage tek bir komutla yapılamıyor:</b>
/// </para>
/// <list type="table">
/// <item>
/// <term>HEAD yok (ilk commit öncesi)</term>
/// <description><c>git restore --staged</c> <b>çöküyor</b>:
/// <c>fatal: could not resolve 'HEAD'</c> (çıkış 128). <c>git rm --cached</c> gerekiyor.</description>
/// </item>
/// <item>
/// <term>HEAD var, dosya HEAD'de yok</term>
/// <description><c>restore --staged</c> doğru: dosya untracked'e döner.</description>
/// </item>
/// <item>
/// <term>HEAD var, dosya HEAD'de var</term>
/// <description><c>restore --staged</c> doğru. <c>rm --cached</c> <b>yanlış</b> olurdu:
/// dosya <i>silinmiş</i> olarak stage'lenir — kullanıcı unstage isterken silme görür.</description>
/// </item>
/// </list>
/// </remarks>
public sealed class StagingWriter : IStagingWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public StagingWriter(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public Task StageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default)
    {
        if (IsEmpty(paths))
        {
            return Task.CompletedTask;
        }

        // `-A`: dosya silinmişse silmeyi de al. Onsuz silinen dosyalar sessizce atlanırdı.
        return _writer.RunAsync(
            workingDirectory,
            ["add", "-A", "--", .. Values(paths)],
            cancellationToken);
    }

    public async Task UnstageAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default)
    {
        if (IsEmpty(paths))
        {
            return;
        }

        bool hasHead = await HasCommitsAsync(workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        // HEAD yoksa `restore --staged` çöküyor (ölçüldü); tek çare `rm --cached`. O durumda
        // dosya zaten HEAD'de olmadığı için "silinmiş olarak stage'leme" riski de yok.
        IReadOnlyList<string> arguments = hasHead
            ? ["restore", "--staged", "--", .. Values(paths)]
            : ["rm", "--cached", "--quiet", "--", .. Values(paths)];

        await _writer.RunAsync(workingDirectory, arguments, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default) =>
        ApplyPatchAsync(
            workingDirectory, diff, selection, PatchDirection.Stage, contentEncoding, cancellationToken);

    public Task UnstagePartialAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        System.Text.Encoding? contentEncoding = null,
        CancellationToken cancellationToken = default) =>
        ApplyPatchAsync(
            workingDirectory, diff, selection, PatchDirection.Unstage, contentEncoding, cancellationToken);

    /// <summary>
    /// Yamayı üretir ve <c>git apply --cached</c> ile uygular.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>--cached</c>: yama <b>yalnızca index'e</b> uygulanır, çalışma ağacına dokunulmaz —
    /// kısmi stage'in tanımı bu.
    /// </para>
    /// <para>
    /// Yama <b>stdin</b> ile geçiriliyor; geçici dosya yok, kabuk yorumlaması yok.
    /// </para>
    /// <para>
    /// ⚠️ <c>--recount</c> <b>kullanılmıyor</b>: yanlış sayıları düzeltip yamayı kabul
    /// ettiriyor ve git'in bize sunduğu tek doğrulamayı kapatırdı (ölçüldü).
    /// </para>
    /// </remarks>
    private async Task ApplyPatchAsync(
        string workingDirectory,
        FileDiff diff,
        PatchSelection selection,
        PatchDirection direction,
        System.Text.Encoding? contentEncoding,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string? patch = PatchBuilder.Build(diff, selection, direction);

        if (patch is null)
        {
            // Seçilen bir şey yok: sessizce hiçbir şey yapma.
            return;
        }

        List<string> arguments = ["apply", "--cached"];

        if (direction == PatchDirection.Unstage)
        {
            arguments.Add("--reverse");
        }

        arguments.Add("-");

        // Yama, diff'in okunduğu kodlamayla baytlanmalı: git onu çalışma ağacındaki
        // baytlarla karşılaştırıyor (P04-T07'deki kodlama mimarisinin yazma tarafı).
        await _writer
            .RunAsync(workingDirectory, arguments, patch, contentEncoding, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task UntrackAsync(
        string workingDirectory,
        IReadOnlyList<RepositoryPath> paths,
        CancellationToken cancellationToken = default)
    {
        if (IsEmpty(paths))
        {
            return Task.CompletedTask;
        }

        return _writer.RunAsync(
            workingDirectory,
            ["rm", "--cached", "--quiet", "--", .. Values(paths)],
            cancellationToken);
    }

    /// <summary>
    /// Depoda en az bir commit var mı?
    /// </summary>
    /// <remarks>
    /// Hata mesajına bakıp karar vermek yerine önden soruluyor: mesaj metni git sürümüne
    /// göre değişebilir, <c>rev-parse</c> ise ~1 ms.
    /// </remarks>
    private async Task<bool> HasCommitsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunCheckedAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-parse", "--verify", "--quiet", "HEAD"],

                // Doğmamış HEAD hata değil, bilgidir.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    private static bool IsEmpty(IReadOnlyList<RepositoryPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // Yol verilmeden `git add -A --` çalıştırmak TÜM depoyu stage'lerdi; boş liste
        // "hiçbir şey yapma" demektir.
        return paths.Count == 0;
    }

    private static IEnumerable<string> Values(IReadOnlyList<RepositoryPath> paths) =>
        paths.Select(path => path.Value);
}
