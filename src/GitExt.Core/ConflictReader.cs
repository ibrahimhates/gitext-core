using GitExt.Core.Git;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Çakışan bir dosyanın index'teki üç sürümünden biri (P07-T02).
/// </summary>
/// <remarks>
/// Numaralar git'in kendi stage numaraları; <c>git show :2:&lt;yol&gt;</c> gibi
/// gösterimlerde aynen kullanılıyor.
/// </remarks>
public enum ConflictStage
{
    /// <summary>Ortak ata (<c>:1:</c>).</summary>
    Base = 1,

    /// <summary>Bizim sürümümüz — <c>HEAD</c> (<c>:2:</c>).</summary>
    Ours = 2,

    /// <summary>Karşı tarafın sürümü (<c>:3:</c>).</summary>
    Theirs = 3,
}

/// <summary>
/// Çakışan tek bir dosya (P07-T01).
/// </summary>
public sealed record ConflictedFile
{
    public required RepositoryPath Path { get; init; }

    public required ConflictKind Kind { get; init; }

    /// <summary>Ortak ata sürümü index'te var mı?</summary>
    public bool HasBase { get; init; }

    /// <summary>Bizim sürümümüz index'te var mı?</summary>
    public bool HasOurs { get; init; }

    /// <summary>Karşı tarafın sürümü index'te var mı?</summary>
    public bool HasTheirs { get; init; }

    /// <summary>Bir alt-modül çakışması mı?</summary>
    public bool IsSubmodule { get; init; }

    /// <summary>Verilen aşama index'te var mı?</summary>
    /// <remarks>
    /// 🔴 Bunu sormadan <c>git show :2:&lt;yol&gt;</c> çalıştırmak <b>fatal</b> veriyor
    /// (ölçüldü: <c>is in the index, but not at stage 2</c>). Hatayı yutup boş metin
    /// döndürmek "dosya boştu" gibi okunurdu — silinmiş bir dosyayla boş bir dosya
    /// kullanıcı için çok farklı şeyler.
    /// </remarks>
    public bool HasStage(ConflictStage stage) => stage switch
    {
        ConflictStage.Base => HasBase,
        ConflictStage.Ours => HasOurs,
        ConflictStage.Theirs => HasTheirs,
        _ => false,
    };

    /// <summary>
    /// Bu çakışma <b>içerik</b> düzeyinde mi, yoksa <b>varlık</b> düzeyinde mi?
    /// </summary>
    /// <remarks>
    /// Varlık çakışmasında (bir taraf silmiş) üç yollu metin görünümü anlamsız: birleştirecek
    /// iki metin yok, verilecek bir karar var — "sil" ya da "tut".
    /// </remarks>
    public bool IsContentConflict => HasOurs && HasTheirs;
}

/// <summary>Çakışma okuma (P07-T01, P07-T02).</summary>
public interface IConflictReader
{
    /// <summary>Çakışan dosyaları listeler.</summary>
    Task<IReadOnlyList<ConflictedFile>> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Çakışan bir dosyanın verilen aşamadaki içeriğini okur.
    /// </summary>
    /// <returns>Aşama index'te yoksa <see langword="null"/>.</returns>
    Task<byte[]?> ReadStageAsync(
        string workingDirectory,
        RepositoryPath path,
        ConflictStage stage,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Index'teki çakışmaları okur (P07-T01, P07-T02).
/// </summary>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ — <c>u</c> satırının düzeni:</b>
/// <c>u &lt;XY&gt; &lt;sub&gt; &lt;m1&gt; &lt;m2&gt; &lt;m3&gt; &lt;mW&gt; &lt;h1&gt; &lt;h2&gt; &lt;h3&gt; &lt;yol&gt;</c>.
/// Eksik bir aşamanın <b>modu</b> <c>000000</c> geliyor — hangi sürümlerin var olduğu
/// buradan biliniyor, deneme yanılmayla değil.
/// </para>
/// <para>
/// 🔴 <b><c>-z</c> zorunlu.</b> Ölçüldü: <c>-z</c> olmadan git yolu C-tırnaklıyor
/// (<c>şğüıöç.txt</c> → <c>"\305\237\304\237…"</c>). Türkçe yollar sessizce bozulurdu.
/// </para>
/// <para>
/// Neden <see cref="StatusReader"/> yetmiyor? O, çakışmanın <b>türünü</b> veriyor ama
/// hangi aşamaların var olduğunu düşürüyor; üç yollu görünüm tam olarak buna dayanıyor.
/// </para>
/// </remarks>
public sealed class ConflictReader : IConflictReader
{
    /// <summary>Var olmayan bir aşamanın modu.</summary>
    private const string AbsentMode = "000000";

    private readonly IGitProcessRunner _runner;

    public ConflictReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<ConflictedFile>> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["status", "--porcelain=v2", "-z", "--untracked-files=no"],
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Parse(result.GetStandardOutputText()) : [];
    }

    public async Task<byte[]?> ReadStageAsync(
        string workingDirectory,
        RepositoryPath path,
        ConflictStage stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (path.IsEmpty)
        {
            return null;
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(
                workingDirectory,
                "show",
                $":{(int)stage}:{path.Value}"),
            cancellationToken).ConfigureAwait(false);

        // Aşama yoksa git `fatal: … but not at stage N` diyor. Bu bir hata değil, bir
        // DURUM: "bu tarafta dosya yok". null ile boş dosyayı ayırmak şart.
        //
        // Ham baytlar dönüyor: içerik metin olmayabilir, olsa bile kodlaması deponun
        // kendi kodlaması. Metne çevirme kararı üst katmanın (P04'ün kodlama tespiti).
        return result.IsSuccess ? result.StandardOutput : null;
    }

    /// <summary><c>--porcelain=v2 -z</c> çıktısındaki <c>u</c> kayıtlarını ayrıştırır.</summary>
    internal static IReadOnlyList<ConflictedFile> Parse(string output)
    {
        List<ConflictedFile> files = [];

        foreach (string record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!record.StartsWith("u ", StringComparison.Ordinal))
            {
                continue;
            }

            // Yol boşluk içerebilir; ilk 10 alan sabit, kalanı yol.
            string[] parts = record.Split(' ', 11);

            if (parts.Length < 11 || !RepositoryPath.TryParse(parts[10], out RepositoryPath path))
            {
                continue;
            }

            files.Add(new ConflictedFile
            {
                Path = path,
                Kind = StatusReader.ParseConflict(parts[1]),
                IsSubmodule = parts[2].StartsWith('S'),
                HasBase = !string.Equals(parts[3], AbsentMode, StringComparison.Ordinal),
                HasOurs = !string.Equals(parts[4], AbsentMode, StringComparison.Ordinal),
                HasTheirs = !string.Equals(parts[5], AbsentMode, StringComparison.Ordinal),
            });
        }

        return files;
    }
}
