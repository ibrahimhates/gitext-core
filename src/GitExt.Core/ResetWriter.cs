using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// <c>git reset</c> modu (P07-T06).
/// </summary>
/// <remarks>
/// Sıra GitExtensions <c>FormResetCurrentBranch</c>'ten (§ 9): en zararsızdan en yıkıcıya.
/// </remarks>
public enum ResetMode
{
    /// <summary>
    /// <c>--soft</c>: yalnızca <c>HEAD</c> oynar.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: dosya diskte <b>yeni</b> içeriğiyle kalıyor ve index'te <b>stage'li</b>
    /// görünüyor (<c>M.</c>) — yani commit geri alınıp değişiklik commit'lenmeye hazır
    /// bekliyor. "Commit'i böl" ya da "mesajı düzelt" senaryosunun aracı.
    /// </remarks>
    Soft,

    /// <summary>
    /// <c>--mixed</c> (git'in varsayılanı): <c>HEAD</c> ve index oynar.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: dosya diskte yeni içeriğiyle kalıyor ama artık <b>stage'siz</b>
    /// (<c>.M</c>). Değişiklik durur, yeniden seçerek stage'lemek gerekir.
    /// </remarks>
    Mixed,

    /// <summary>
    /// <c>--hard</c>: <c>HEAD</c>, index <b>ve çalışma ağacı</b> oynar.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>YIKICI.</b> ÖLÇÜLDÜ: dosya eski içeriğine döndü ve çalışma ağacı
    /// <b>tertemiz</b>. Commit'lenmemiş her değişiklik <b>kaybolur</b> ve reflog bunu
    /// geri getirmez — reflog yalnızca commit'leri tutar. Bu yüzden ayrı bir onay
    /// gerektiriyor.
    /// </remarks>
    Hard,

    /// <summary>
    /// <c>--keep</c>: <c>HEAD</c> oynar ama yerel değişiklikler korunur.
    /// </summary>
    /// <remarks>
    /// ÖLÇÜLDÜ: ilgisiz yerel değişiklikler ayakta kaldı. Çakışırsa git reddediyor —
    /// yani <c>--hard</c>'ın "önce sor" hali.
    /// </remarks>
    Keep,
}

/// <summary>Reset seçenekleri (P07-T06).</summary>
public sealed record ResetOptions
{
    /// <summary>Dönülecek commit ya da referans.</summary>
    public required string Target { get; init; }

    public ResetMode Mode { get; init; } = ResetMode.Mixed;
}

/// <summary>
/// Reset'in <b>ne yapacağının</b> önizlemesi (P07-T06).
/// </summary>
/// <remarks>
/// Plan bunu açıkça istiyor: <i>"Her modun ne yapacağını açıkça anlatan bir diyalog:
/// hangi commit'ler kaybolacak, çalışma dizinine ne olacak."</i>
/// </remarks>
public sealed record ResetPreview
{
    /// <summary>Hedeften sonra gelen, yani <c>HEAD</c>'den düşecek commit'ler.</summary>
    public IReadOnlyList<string> DroppedCommits { get; init; } = [];

    public int DroppedCount => DroppedCommits.Count;

    /// <summary>Çalışma ağacında commit'lenmemiş değişiklik var mı?</summary>
    public bool HasUncommittedChanges { get; init; }

    /// <summary>Hedef geçerli bir commit'e çözülüyor mu?</summary>
    public required bool IsTargetValid { get; init; }

    /// <summary>Hedefin tam SHA'sı.</summary>
    public string TargetObjectId { get; init; } = string.Empty;

    /// <summary>
    /// Bu modla <b>geri alınamayacak</b> bir kayıp olur mu?
    /// </summary>
    /// <remarks>
    /// Düşen commit'ler reflog'da durduğu için geri alınabilir; asıl geri alınamayan şey
    /// <c>--hard</c>'ın sildiği <b>commit'lenmemiş</b> değişiklikler.
    /// </remarks>
    public bool LosesUncommittedWork(ResetMode mode) =>
        mode == ResetMode.Hard && HasUncommittedChanges;
}

/// <summary>Reset işlemleri (P07-T06).</summary>
public interface IResetWriter
{
    Task<SafetyPoint> ResetAsync(
        string workingDirectory,
        ResetOptions options,
        CancellationToken cancellationToken = default);

    Task<ResetPreview> PreviewAsync(
        string workingDirectory,
        string target,
        CancellationToken cancellationToken = default);

    string DescribeCommand(ResetOptions options);
}

/// <summary>
/// <c>git reset</c> sarmalayıcısı (P07-T06).
/// </summary>
public sealed class ResetWriter : IResetWriter
{
    private readonly IGitWriter _writer;
    private readonly IGitProcessRunner _runner;
    private readonly ISafetyPointRecorder _safety;

    public ResetWriter(IGitWriter writer, IGitProcessRunner runner, ISafetyPointRecorder safety)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(safety);

        _writer = writer;
        _runner = runner;
        _safety = safety;
    }

    /// <returns>İşlem <b>öncesi</b> konum — geri alma bilgisi bunun üzerinden veriliyor.</returns>
    public async Task<SafetyPoint> ResetAsync(
        string workingDirectory,
        ResetOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Target);

        // Faz kuralı: geçmişi değiştiren her işlemden ÖNCE konum kaydedilir.
        SafetyPoint point = await _safety
            .CaptureAsync(workingDirectory, "reset", cancellationToken)
            .ConfigureAwait(false);

        await _writer
            .RunAsync(workingDirectory, BuildArguments(options), cancellationToken)
            .ConfigureAwait(false);

        return point;
    }

    public async Task<ResetPreview> PreviewAsync(
        string workingDirectory,
        string target,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        GitResult resolved = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, "rev-parse", "--verify", "--quiet", $"{target}^{{commit}}"),
            cancellationToken).ConfigureAwait(false);

        if (!resolved.IsSuccess || resolved.GetStandardOutputText().Trim().Length == 0)
        {
            return new ResetPreview { IsTargetValid = false };
        }

        // Hedeften HEAD'e kadar olanlar düşecek olanlar. `--oneline` değil, konu ayrı
        // okunuyor: insan biçimli çıktı ayrıştırmak ADR-0002'ye aykırı.
        GitResult dropped = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory, "log", "--format=%H%x00%s%x00%x00", $"{target}..HEAD"),
            cancellationToken).ConfigureAwait(false);

        List<string> commits = [];

        if (dropped.IsSuccess)
        {
            foreach (string record in dropped.GetStandardOutputText().Split("\0\0"))
            {
                string trimmed = record.TrimStart('\n', '\r');
                string[] fields = trimmed.Split('\0');

                if (fields.Length >= 2 && fields[0].Length > 0)
                {
                    commits.Add(fields[1]);
                }
            }
        }

        GitResult status = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["status", "--porcelain=v2", "-z", "--untracked-files=no"],
            },
            cancellationToken).ConfigureAwait(false);

        return new ResetPreview
        {
            IsTargetValid = true,
            TargetObjectId = resolved.GetStandardOutputText().Trim(),
            DroppedCommits = commits,
            HasUncommittedChanges = status.IsSuccess && status.GetStandardOutputText().Length > 0,
        };
    }

    public string DescribeCommand(ResetOptions options) => Describe(options);

    /// <summary>Çalıştırılacak komutu üretir ("komutu göster" ilkesi).</summary>
    public static string Describe(ResetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return "git " + string.Join(' ', BuildArguments(options));
    }

    /// <remarks>
    /// 🔴 <b>Ayracın YERİ burada diğer komutlardan farklı.</b> İlk yazımda merge'den
    /// kopyalanan <c>… -- &lt;hedef&gt;</c> deyimi kullanılmıştı ve ÖLÇÜMDE
    /// <c>fatal: Cannot do hard reset with paths</c> ile öldü: <c>reset</c> için
    /// <c>--</c>'dan sonrası <b>yol</b> demek, commit demek değil.
    /// <para>
    /// Doğrusu ayracı <b>sona</b> koymak. Gereksiz de değil: bir dal adıyla aynı adda bir
    /// dosya varken ayraçsız çağrı <c>fatal: ambiguous argument … both revision and
    /// filename</c> veriyor, sondaki <c>--</c> ile sorun kalmıyor (ölçüldü).
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> BuildArguments(ResetOptions options) =>
    [
        "reset",
        options.Mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Hard => "--hard",
            ResetMode.Keep => "--keep",
            _ => "--mixed",
        },
        options.Target,
        "--",
    ];
}
