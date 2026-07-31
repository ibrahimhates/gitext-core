using System.Text;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// Yamanın hangi yönde uygulanacağı (P05-T04).
/// </summary>
public enum PatchDirection
{
    /// <summary>
    /// İleri: çalışma ağacındaki değişikliği index'e taşı (<c>git apply --cached</c>).
    /// </summary>
    Stage,

    /// <summary>
    /// Geri: index'teki değişikliği geri al (<c>git apply --cached --reverse</c>).
    /// </summary>
    /// <remarks>
    /// Bu yönde yamanın kaynağı <c>git diff --cached</c> olmalıdır — yani index ile
    /// <c>HEAD</c> arasındaki fark.
    /// </remarks>
    Unstage,
}

/// <summary>
/// Bir dosya diff'inde hangi satırların seçildiği (P05-T04).
/// </summary>
public sealed class PatchSelection
{
    private readonly HashSet<(int Hunk, int Line)> _lines;

    private PatchSelection(HashSet<(int Hunk, int Line)> lines)
    {
        _lines = lines;
    }

    /// <summary>Belirli satırları seçer.</summary>
    public static PatchSelection Lines(IEnumerable<(int Hunk, int Line)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new PatchSelection([.. lines]);
    }

    /// <summary>Verilen hunk'ların <b>tüm</b> değişiklik satırlarını seçer.</summary>
    public static PatchSelection Hunks(FileDiff diff, params int[] hunkIndexes)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(hunkIndexes);

        HashSet<(int, int)> lines = [];

        foreach (int hunkIndex in hunkIndexes)
        {
            DiffHunk hunk = diff.Hunks[hunkIndex];

            for (int line = 0; line < hunk.Lines.Count; line++)
            {
                if (hunk.Lines[line].Kind != DiffLineKind.Context)
                {
                    lines.Add((hunkIndex, line));
                }
            }
        }

        return new PatchSelection(lines);
    }

    /// <summary>Dosyadaki tüm değişiklik satırlarını seçer.</summary>
    public static PatchSelection All(FileDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return Hunks(diff, [.. Enumerable.Range(0, diff.Hunks.Count)]);
    }

    public bool IsSelected(int hunkIndex, int lineIndex) => _lines.Contains((hunkIndex, lineIndex));

    public int Count => _lines.Count;
}

/// <summary>
/// Seçilen satırlardan <c>git apply</c>'a verilebilecek bir yama üretir (P05-T04).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bu fazın en riskli kodu.</b> Ölçümle netleşen risk dağılımı:
/// </para>
/// <list type="table">
/// <item><term>Hunk başlığındaki sayılar yanlış</term>
/// <description><c>error: corrupt patch</c> — git <b>reddediyor</b>.</description></item>
/// <item><term>Bağlam veya silinen satır dosyayla uyuşmuyor</term>
/// <description><c>error: patch failed</c> — git <b>reddediyor</b>.</description></item>
/// <item><term><b>Seçim mantığı yanlış</b></term>
/// <description>Yama <b>geçerli</b> olduğu için git kabul eder ve <b>sessizce yanlış içerik</b>
/// stage'lenir. Ölçüldü: seçilmeyen bir <c>-</c> satırı bağlama çevrilmezse o satır
/// index'ten kayboluyor. <b>Testlerin asıl odağı budur.</b></description></item>
/// </list>
/// <para>
/// <b><c>--recount</c> bilinçli olarak KULLANILMIYOR.</b> Ölçüldü: yanlış sayıları düzeltip
/// yamayı kabul ettiriyor. Bu, git'in bize sunduğu tek doğrulamayı kapatmak olurdu — sayı
/// hatası genelde daha derin bir mantık hatasının belirtisidir.
/// </para>
/// </remarks>
public static class PatchBuilder
{
    /// <summary>
    /// Seçili satırlardan yama üretir; seçilecek bir şey yoksa <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Satır dönüşüm kuralları — <b>yöne göre simetrik</b>:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Stage:</b> seçilmeyen <c>+</c> satırı <b>atlanır</b> (henüz
    /// index'te yok, kalmasın), seçilmeyen <c>-</c> satırı <b>bağlama çevrilir</b> (index'te
    /// var, kalmalı).</description></item>
    /// <item><description><b>Unstage:</b> tam tersi — seçilmeyen <c>-</c> satırı atlanır,
    /// seçilmeyen <c>+</c> satırı bağlama çevrilir. Çünkü yama ters uygulanacak ve
    /// "eski/yeni" rolleri yer değiştiriyor.</description></item>
    /// </list>
    /// </remarks>
    public static string? Build(FileDiff diff, PatchSelection selection, PatchDirection direction)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Count == 0 || !diff.HasHunks)
        {
            return null;
        }

        StringBuilder body = new();
        int emitted = 0;
        int delta = 0;

        for (int hunkIndex = 0; hunkIndex < diff.Hunks.Count; hunkIndex++)
        {
            DiffHunk hunk = diff.Hunks[hunkIndex];

            List<(DiffLineKind Kind, DiffLine Line)> lines = [];
            bool hasChange = false;

            for (int lineIndex = 0; lineIndex < hunk.Lines.Count; lineIndex++)
            {
                DiffLine line = hunk.Lines[lineIndex];

                if (line.Kind == DiffLineKind.Context)
                {
                    lines.Add((DiffLineKind.Context, line));
                    continue;
                }

                if (selection.IsSelected(hunkIndex, lineIndex))
                {
                    lines.Add((line.Kind, line));
                    hasChange = true;
                    continue;
                }

                if (KeepAsContext(line.Kind, direction))
                {
                    lines.Add((DiffLineKind.Context, line));
                }

                // Diğer durumda satır tamamen atlanır.
            }

            if (!hasChange)
            {
                // Bu hunk'tan hiçbir şey seçilmedi; yamaya girmemeli.
                continue;
            }

            int oldCount = lines.Count(entry => entry.Kind != DiffLineKind.Added);
            int newCount = lines.Count(entry => entry.Kind != DiffLineKind.Removed);

            body.Append(FormatHunkHeader(hunk.OldStart, oldCount, hunk.OldStart + delta, newCount));
            body.Append('\n');

            foreach ((DiffLineKind kind, DiffLine line) in lines)
            {
                body.Append(Prefix(kind));
                body.Append(line.Content);
                body.Append('\n');

                // ⚠️ İşaret satırdan SONRA gelir ve ilgili olduğu satıra aittir (P04-T01'de
                // ölçüldü). Atlanırsa git yamayı reddeder ya da dosya sonuna newline ekler.
                if (line.EndsWithoutNewline)
                {
                    body.Append("\\ No newline at end of file\n");
                }
            }

            delta += newCount - oldCount;
            emitted++;
        }

        if (emitted == 0)
        {
            return null;
        }

        return Header(diff) + body.ToString();
    }

    /// <summary>
    /// Seçilmemiş bir değişiklik satırı bağlam olarak korunmalı mı?
    /// </summary>
    private static bool KeepAsContext(DiffLineKind kind, PatchDirection direction) =>
        direction == PatchDirection.Stage
            ? kind == DiffLineKind.Removed
            : kind == DiffLineKind.Added;

    private static char Prefix(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added => '+',
        DiffLineKind.Removed => '-',
        _ => ' ',
    };

    /// <summary>
    /// Hunk başlığını biçimlendirir.
    /// </summary>
    /// <remarks>
    /// Tek satırlık taraflarda git sayıyı yazmıyor (<c>@@ -1 +1 @@</c>); aynı biçim
    /// üretiliyor ki yama git'in kendi çıktısıyla karşılaştırılabilir olsun.
    /// </remarks>
    private static string FormatHunkHeader(int oldStart, int oldCount, int newStart, int newCount)
    {
        // Boş taraf 0 satır uzunluğundadır ve başlangıcı bir eksiğiyle yazılır (git böyle yapıyor).
        string old = oldCount == 1 ? $"-{oldStart}" : $"-{(oldCount == 0 ? oldStart - 1 : oldStart)},{oldCount}";
        string @new = newCount == 1 ? $"+{newStart}" : $"+{(newCount == 0 ? newStart - 1 : newStart)},{newCount}";

        return $"@@ {old} {@new} @@";
    }

    /// <summary>
    /// Dosya başlığını üretir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ÖLÇÜLDÜ:</b> <c>git apply</c> yolları <b>ham UTF-8</b> olarak kabul ediyor;
    /// okuma tarafındaki (P04-T02) tırnaklama/oktal kaçış <b>gerekmiyor</b>. Boşluklu adlar
    /// da tırnaksız çalışıyor.
    /// </para>
    /// <para>
    /// <b>Yama TEK PARÇA Unicode metindir.</b> <c>DiffLine.Content</c> ayrıştırıcıdan
    /// <c>DiffOptions.ContentEncoding</c> ile <b>çözülmüş</b> olarak geliyor (P04-T07), yol
    /// da öyle. Dolayısıyla yama, dosyanın kodlamasıyla <b>bir kez</b> baytlanır.
    /// </para>
    /// <para>
    /// ⚠️ Bu ölçümle öğrenildi: içerik kayıpsız (bayt-başına-karakter) sanılıp Latin-1 ile
    /// baytlanınca <c>ı</c> gibi karakterler bozuluyor ve <c>git apply</c>
    /// <c>error: while searching for: ilk satir</c> diyerek yamayı reddediyordu.
    /// </para>
    /// </remarks>
    private static string Header(FileDiff diff)
    {
        string oldPath = (diff.OldPath ?? diff.Path).Value;
        string newPath = diff.Path.Value;

        return $"diff --git a/{oldPath} b/{newPath}\n--- a/{oldPath}\n+++ b/{newPath}\n";
    }
}
