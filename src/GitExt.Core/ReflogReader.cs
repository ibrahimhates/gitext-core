using System.Globalization;
using GitExt.Core.Git;

namespace GitExt.Core;

/// <summary>
/// Reflog girdisinin ne yüzünden oluştuğu (P07-T14).
/// </summary>
/// <remarks>
/// git bunu ayrı bir alan olarak vermiyor; <c>%gs</c> metninin <b>ilk kelimesinden</b>
/// çıkarılıyor (<c>commit:</c>, <c>reset:</c>, <c>rebase (finish):</c> …). Metin
/// yerelleştirilmiyor — git reflog eylem adlarını çevirmiyor (ölçüldü).
/// </remarks>
public enum ReflogAction
{
    /// <summary>Tanınmayan ya da yeni bir eylem.</summary>
    Other,

    Commit,
    Amend,
    Checkout,
    Reset,
    Merge,
    Rebase,
    CherryPick,
    Revert,
    Pull,
    Clone,
    Branch,
    Stash,
}

/// <summary>
/// Tek bir reflog girdisi (P07-T14).
/// </summary>
public sealed record ReflogEntry
{
    /// <summary>Girdinin işaret ettiği commit (tam SHA).</summary>
    public required string ObjectId { get; init; }

    /// <summary>Seçici — <c>HEAD@{3}</c> ya da <c>refs/heads/main@{2}</c>.</summary>
    public required string Selector { get; init; }

    /// <summary>Ham eylem metni (<c>%gs</c>), örn. <c>reset: moving to HEAD~1</c>.</summary>
    public required string Message { get; init; }

    /// <summary>Girdinin ait olduğu commit'in konusu (<c>%s</c>).</summary>
    public string Subject { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    public string AuthorName { get; init; } = string.Empty;

    public ReflogAction Action { get; init; }

    /// <summary>
    /// Bu girdi <b>şu anki</b> HEAD'den erişilemeyen bir commit'i mi gösteriyor?
    /// </summary>
    /// <remarks>
    /// Reflog tarayıcısının asıl işi bu: "kaybolmuş" commit'i bulmak. Değer okuyucu
    /// tarafından doldurulur; <see cref="ReflogReader"/> bunu ayrı bir sorguyla hesaplar.
    /// </remarks>
    public bool IsUnreachable { get; init; }

    /// <summary>Kısaltılmış SHA — listede gösterilen.</summary>
    public string ShortId => ObjectId.Length >= 7 ? ObjectId[..7] : ObjectId;

    /// <summary>
    /// Bu girdiye dönmek için çalıştırılacak komut.
    /// </summary>
    /// <remarks>
    /// ⚠️ Seçici (<c>HEAD@{3}</c>) <b>değil</b> SHA yazılıyor. Seçici <b>kayan</b> bir
    /// referans: yeni bir işlem reflog'a girdi eklediğinde <c>HEAD@{3}</c> bambaşka bir
    /// commit'i gösterir. Kullanıcı komutu kopyalayıp beş dakika sonra çalıştırırsa
    /// yanlış yere dönerdi. (P06-T07'de <c>ORIG_HEAD</c> ile aynı ders.)
    /// </remarks>
    public string RecoveryCommand => $"git reset --hard {ObjectId}";
}

/// <summary>Reflog okuma (P07-T14).</summary>
public interface IReflogReader
{
    /// <summary>
    /// Reflog girdilerini okur.
    /// </summary>
    /// <param name="workingDirectory">Deponun çalışma dizini.</param>
    /// <param name="reference">
    /// <c>HEAD</c>, bir dal adı, ya da tümü için <see langword="null"/>.
    /// </param>
    /// <param name="limit">En fazla kaç girdi okunacağı.</param>
    /// <param name="cancellationToken">İptal belirteci.</param>
    Task<IReadOnlyList<ReflogEntry>> ReadAsync(
        string workingDirectory,
        string? reference = null,
        int limit = 200,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>git reflog</c> okuyucusu (P07-T14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu sınıf fazın sigortası.</b> Faz 07'deki her işlem geçmişi yeniden yazıyor;
/// kullanıcı bir şeyi kaybettiğinde onu buradan geri alacak. Plan bu yüzden "faz içinde
/// erken yapılmalı, sonuna bırakılmamalı" diyor.
/// </para>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ — TAB ayırıcı güvenli değil.</b> Commit mesajı sekme içerebiliyor ve
/// <c>%s</c> onu <b>olduğu gibi</b> basıyor; sekmeyle bölen bir ayrıştırıcı fazladan alan
/// görür ve satırı kaydırır. (İlginç biçimde <c>%gs</c> sekmeyi boşluğa çeviriyor, <c>%s</c>
/// çevirmiyor — yani "bir alan güvenli" diğerini garanti etmiyor.) Bu yüzden alanlar
/// <b>NUL</b> ile ayrılıyor; NUL bir commit mesajında bulunamaz.
/// </para>
/// <para>
/// ℹ️ <b>ÖLÇÜLDÜ — <c>git fsck</c> gerekmiyor.</b> <c>reset --hard</c> ile "kaybolan"
/// commit reflog'da duruyor; erişilemeyen nesneleri taramaya gerek yok.
/// </para>
/// </remarks>
public sealed class ReflogReader : IReflogReader
{
    /// <summary>Alan ayırıcı — NUL.</summary>
    private const char FieldSeparator = '\0';

    /// <summary>Kayıt ayırıcı — iki NUL.</summary>
    private const string RecordSeparator = "\0\0";

    private const string Format = "%H%x00%gD%x00%gs%x00%s%x00%ct%x00%an%x00%x00";

    private readonly IGitProcessRunner _runner;

    public ReflogReader(IGitProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<IReadOnlyList<ReflogEntry>> ReadAsync(
        string workingDirectory,
        string? reference = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        List<string> arguments =
        [
            "reflog",
            "show",
            $"--format={Format}",
            $"--max-count={limit.ToString(CultureInfo.InvariantCulture)}",
        ];

        if (reference is { Length: > 0 } target)
        {
            // `--` ayracı yok: `reflog show` yol almıyor, ama `-` ile başlayan bir dal adı
            // bayrak sanılırdı. `--all` ise ayrı bir bayrak, ref olarak geçemez.
            arguments.Add(target);
        }
        else
        {
            arguments.Add("--all");
        }

        GitResult result = await _runner.RunAsync(
            GitCommand.Create(workingDirectory, [.. arguments]),
            cancellationToken).ConfigureAwait(false);

        // Reflog'u olmayan bir depo (henüz commit yok) hata veriyor; bu boş liste demek.
        if (!result.IsSuccess)
        {
            return [];
        }

        IReadOnlyList<ReflogEntry> entries = Parse(result.GetStandardOutputText());

        return await MarkUnreachableAsync(workingDirectory, entries, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>NUL ayrılmış reflog çıktısını ayrıştırır.</summary>
    internal static IReadOnlyList<ReflogEntry> Parse(string output)
    {
        List<ReflogEntry> entries = [];

        foreach (string record in output.Split(RecordSeparator, StringSplitOptions.None))
        {
            // Kayıtlar arasında satır sonu kalıyor (format'ın sonundaki `%x00%x00` git'in
            // eklediği `\n`den önce geliyor); baştaki boşluk temizleniyor.
            string trimmed = record.TrimStart('\n', '\r');

            if (trimmed.Length == 0)
            {
                continue;
            }

            string[] fields = trimmed.Split(FieldSeparator);

            // Alan sayısı tutmuyorsa satır bizim değil; uydurmak yerine atlanıyor.
            if (fields.Length < 6 || fields[0].Length == 0)
            {
                continue;
            }

            entries.Add(new ReflogEntry
            {
                ObjectId = fields[0],
                Selector = fields[1],
                Message = fields[2],
                Subject = fields[3],
                Timestamp = ParseTimestamp(fields[4]),
                AuthorName = fields[5],
                Action = ClassifyAction(fields[2]),
            });
        }

        return entries;
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        long.TryParse(value, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : default;

    /// <summary>
    /// <c>%gs</c> metninden eylemi çıkarır.
    /// </summary>
    /// <remarks>
    /// Metin <c>commit: …</c>, <c>commit (amend): …</c>, <c>rebase (finish): …</c> gibi
    /// biçimlerde geliyor. İlk iki nokta üst üsteye kadarki kısım alınıp içindeki parantezli
    /// ek de dikkate alınıyor.
    /// </remarks>
    internal static ReflogAction ClassifyAction(string message)
    {
        int colon = message.IndexOf(':', StringComparison.Ordinal);
        ReadOnlySpan<char> head = colon < 0 ? message : message.AsSpan(0, colon);

        // `commit (amend)` ve `commit (initial)` ayrımı: amend geçmişi değiştiriyor.
        if (head.Contains("amend", StringComparison.OrdinalIgnoreCase))
        {
            return ReflogAction.Amend;
        }

        ReadOnlySpan<char> verb = head;
        int space = head.IndexOf(' ');

        if (space > 0)
        {
            verb = head[..space];
        }

        return verb switch
        {
            "commit" => ReflogAction.Commit,
            "checkout" => ReflogAction.Checkout,
            "reset" => ReflogAction.Reset,
            "merge" => ReflogAction.Merge,
            "rebase" => ReflogAction.Rebase,
            "cherry-pick" => ReflogAction.CherryPick,
            "revert" => ReflogAction.Revert,
            "pull" => ReflogAction.Pull,
            "clone" => ReflogAction.Clone,
            "branch" => ReflogAction.Branch,
            "stash" => ReflogAction.Stash,
            _ => ReflogAction.Other,
        };
    }

    /// <summary>
    /// Hangi girdilerin <b>şu anki</b> geçmişten erişilemez olduğunu işaretler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Adaylar <b>stdin</b> ile veriliyor ve <c>--not --all</c> ile eleniyor: git yalnızca
    /// hiçbir ref'ten erişilemeyen commit'leri geri yazıyor. Girdi başına bir
    /// <c>merge-base --is-ancestor</c> çalıştırmak yüzlerce süreç açardı.
    /// </para>
    /// <para>
    /// 🔴 <b>İlk yazımda <c>rev-list --all --no-walk=unsorted HEAD</c> kullanılmıştı ve
    /// YANLIŞTI:</b> <c>--no-walk</c> geçmişi <b>gezmiyor</b>, yalnızca uçları basıyor.
    /// Üç commit'lik bir depoda tek satır döndü; sonuçta ilk commit'ten sonraki <b>her</b>
    /// eski reflog girdisi "kayıp commit" diye işaretlenirdi. Ölçümle yakalandı, testle
    /// sabitlendi.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<ReflogEntry>> MarkUnreachableAsync(
        string workingDirectory,
        IReadOnlyList<ReflogEntry> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return entries;
        }

        string candidates = string.Join(
            '\n',
            entries.Select(entry => entry.ObjectId).Distinct(StringComparer.Ordinal));

        GitResult result = await _runner.RunAsync(
            new GitCommand
            {
                WorkingDirectory = workingDirectory,
                Arguments = ["rev-list", "--no-walk", "--stdin", "--not", "--all"],
                StandardInput = System.Text.Encoding.UTF8.GetBytes(candidates + "\n"),
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // Belirleyemiyorsak "kayıp" DEMİYORUZ: yanlış bir kayıp uyarısı, kullanıcıyı
            // olmayan bir sorunu kovalamaya iter.
            return entries;
        }

        HashSet<string> unreachable = new(StringComparer.Ordinal);

        foreach (string line in result.GetStandardOutputText()
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            unreachable.Add(line.Trim());
        }

        return [.. entries.Select(entry => entry with
        {
            IsUnreachable = unreachable.Contains(entry.ObjectId),
        })];
    }
}
