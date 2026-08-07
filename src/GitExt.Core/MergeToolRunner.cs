using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Harici bir birleştirme aracı (P07-T04).
/// </summary>
public sealed record MergeTool
{
    /// <summary>git'in tanıdığı ad — <c>meld</c>, <c>kdiff3</c>, <c>vscode</c>…</summary>
    public required string Name { get; init; }

    /// <summary>git'in yazdığı açıklama.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Araç bu makinede <b>kurulu</b> mu?
    /// </summary>
    /// <remarks>
    /// <c>git mergetool --tool-help</c> iki liste basıyor: kullanılabilecekler ve
    /// <i>"valid, but not currently available"</i> olanlar. Kurulu olmayanı seçtirmek
    /// kullanıcıyı çalışmayan bir düğmeye tıklatırdı.
    /// </remarks>
    public bool IsAvailable { get; init; }
}

/// <summary>Harici birleştirme aracı entegrasyonu (P07-T04).</summary>
public interface IMergeToolRunner
{
    /// <summary>Kullanıcının <c>merge.tool</c> ayarı; yoksa <see langword="null"/>.</summary>
    Task<string?> GetConfiguredToolAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>git'in tanıdığı araçları listeler.</summary>
    Task<IReadOnlyList<MergeTool>> ListToolsAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Çakışan bir dosya için harici aracı çalıştırır.
    /// </summary>
    /// <param name="workingDirectory">Deponun çalışma dizini.</param>
    /// <param name="path">Çakışan dosya; <see langword="null"/> ise tüm çakışanlar.</param>
    /// <param name="tool">Kullanılacak araç; <see langword="null"/> ise yapılandırılmış olan.</param>
    /// <param name="cancellationToken">İptal belirteci.</param>
    Task<MergeToolResult> RunAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        string? tool = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Harici aracın sonucu (P07-T04).</summary>
public sealed record MergeToolResult
{
    /// <summary>Araç çalıştıktan sonra dosya çözülmüş sayıldı mı?</summary>
    public required bool IsResolved { get; init; }

    /// <summary>Aracın bıraktığı çıktı (kullanıcıya gösterilecek).</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// Aracın geride bıraktığı <c>.orig</c> yedekleri.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>ÖLÇÜLDÜ — <c>git mergetool</c> her dosya için bir <c>&lt;ad&gt;.orig</c>
    /// bırakıyor</b> ve bunlar takip edilmeyen dosya olarak çalışma ağacında kalıyor.
    /// Kullanıcı bunu beklemiyorsa "nereden çıktı bu dosyalar" diye sorar; listelenip
    /// silinmesi teklif ediliyor. (<c>mergetool.keepBackup=false</c> ayarı da bunu kapatır
    /// ama kullanıcının yapılandırmasını <b>biz</b> değiştirmiyoruz.)
    /// </remarks>
    public IReadOnlyList<RepositoryPath> BackupFiles { get; init; } = [];
}

/// <summary>
/// <c>git mergetool</c> sarmalayıcısı (P07-T04).
/// </summary>
/// <remarks>
/// <para>
/// Plandaki gerekçe: <i>"Yerleşik görünümü mükemmelleştirmeye çalışmak yerine, kullanıcının
/// zaten kurduğu aracı desteklemek daha yüksek getirili."</i> Yerleşik üç yollu görünüm
/// (P07-T03) basit çakışmalar için, harici araç karmaşık olanlar için.
/// </para>
/// <para>
/// <c>--no-prompt</c> veriliyor: <c>git mergetool</c> normalde her dosya için
/// <i>"Hit return to start merge resolution tool"</i> diye stdin'den okuyor.
/// ℹ️ <b>ÖLÇÜLDÜ — bu bizim durumumuzda kilitlenmeye yol açmıyor:</b> stdin kapalıyken
/// git EOF okuyup devam ediyor (rc=0, 0 sn). Yani bayrak bir <i>düzeltme</i> değil,
/// bu davranışa bağımlı kalmama tercihi — ve gereksiz istemi çıktıdan uzak tutuyor.
/// </para>
/// </remarks>
public sealed class MergeToolRunner : IMergeToolRunner
{
    private const string UnavailableMarker = "not currently available";

    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;

    public MergeToolRunner(IGitWriter writer, IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);

        _writer = writer;
        _runner = runner;
    }

    public async Task<string?> GetConfiguredToolAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["config", "--get", "merge.tool"],

                // Ayar yoksa `git config --get` çıkış kodu 1 veriyor; bu bir hata değil.
                SuccessExitCodes = [0, 1],
            },
            cancellationToken).ConfigureAwait(false);

        string value = result.GetStandardOutputText().Trim();
        return value.Length == 0 ? null : value;
    }

    public async Task<IReadOnlyList<MergeTool>> ListToolsAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "mergetool", "--tool-help"),
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? ParseToolHelp(result.GetStandardOutputText()) : [];
    }

    /// <summary>
    /// <c>git mergetool --tool-help</c> çıktısını ayrıştırır.
    /// </summary>
    /// <remarks>
    /// Çıktı iki bölüm: önce kullanılabilecekler, sonra <i>"valid, but not currently
    /// available"</i> başlığından sonrakiler. Araç satırları sekmeyle girintili ve
    /// <c>&lt;ad&gt;&lt;boşluklar&gt;&lt;açıklama&gt;</c> biçiminde.
    /// </remarks>
    internal static IReadOnlyList<MergeTool> ParseToolHelp(string output)
    {
        List<MergeTool> tools = [];
        bool available = true;

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.Contains(UnavailableMarker, StringComparison.Ordinal))
            {
                available = false;
                continue;
            }

            // Araç satırları girintili; başlıklar değil.
            if (line.Length == 0 || (line[0] != '\t' && line[0] != ' '))
            {
                continue;
            }

            string trimmed = line.Trim();
            int space = trimmed.IndexOf(' ', StringComparison.Ordinal);

            if (trimmed.Length == 0)
            {
                continue;
            }

            string name = space < 0 ? trimmed : trimmed[..space];

            tools.Add(new MergeTool
            {
                Name = name,
                Description = space < 0 ? string.Empty : trimmed[space..].Trim(),
                IsAvailable = available,
            });
        }

        return tools;
    }

    public async Task<MergeToolResult> RunAsync(
        string workingDirectory,
        RepositoryPath? path = null,
        string? tool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        List<string> arguments = ["mergetool", "--no-prompt"];

        if (tool is { Length: > 0 } name)
        {
            arguments.Add($"--tool={name}");
        }

        if (path is { } target && !target.IsEmpty)
        {
            arguments.Add("--");
            arguments.Add(target.Value);
        }

        GitResult result = await _writer
            .RunAsync(workingDirectory, arguments, cancellationToken)
            .ConfigureAwait(false);

        GitResult conflicts = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "diff", "--name-only", "--diff-filter=U", "-z"),
            cancellationToken).ConfigureAwait(false);

        // Aracın çıkış kodu değil, INDEX'in durumu karar veriyor: bazı araçlar kullanıcı
        // kaydetmeden kapatsa da 0 dönüyor (`trustExitCode` varsayılan olarak kapalı).
        bool resolved = conflicts.IsSuccess
            && conflicts.GetStandardOutputText().Split('\0', StringSplitOptions.RemoveEmptyEntries).Length == 0;

        return new MergeToolResult
        {
            IsResolved = resolved,
            Output = result.GetStandardOutputText(),
            BackupFiles = await FindBackupsAsync(workingDirectory, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Aracın bıraktığı <c>.orig</c> dosyalarını bulur.</summary>
    private async Task<IReadOnlyList<RepositoryPath>> FindBackupsAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory,
                "ls-files", "--others", "--exclude-standard", "-z", "--", "*.orig"),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return [];
        }

        List<RepositoryPath> backups = [];

        foreach (string value in result.GetStandardOutputText()
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (RepositoryPath.TryParse(value, out RepositoryPath path))
            {
                backups.Add(path);
            }
        }

        return backups;
    }
}
