using System.Text;

namespace GitExt.Core.Git;

/// <summary>
/// Bir git komutunun tanı çıktısını (<c>stderr</c>) <b>gösterilebilir</b> metne çevirir (P05-T07).
/// </summary>
/// <remarks>
/// <para>
/// <b>ÖLÇÜLDÜ:</b> git, hook'ların <c>stdout</c>'unu da <c>stderr</c>'e yönlendiriyor
/// (<c>stdout_to_stderr</c>): <c>echo</c> ile yazan bir <c>pre-commit</c> hook'unun satırları
/// <c>stderr</c>'de çıkıyor. Yani hook çıktısının tamamı tek kanalda toplanıyor ve
/// <see cref="GitResult.StandardError"/> onu <b>eksiksiz</b> taşıyor.
/// </para>
/// <para>
/// ⚠️ <b>Hook çıktısı ile git'in kendi çıktısı ayırt EDİLEMEZ.</b> Karışık gelen tek bir akış
/// var ve git hook satırlarına bir işaret koymuyor. Bu yüzden arayüz bu metni "hook çıktısı"
/// diye değil, komutun çıktısı diye sunmalı. (Ölçüldü: hook'suz başarılı bir commit'te
/// <c>stderr</c> <b>tamamen boş</b> — pratikte dolu <c>stderr</c> hook'a işaret ediyor,
/// ama bu bir garanti değil.)
/// </para>
/// <para>
/// Metin ham geliyor: hook'lar ANSI renk kodları ve satır üzerine yazan <c>\r</c> ilerleme
/// çıktısı üretebiliyor (ölçüldü, git ikisini de olduğu gibi geçiriyor). İkisi de bir metin
/// kutusunda okunaksız görünür.
/// </para>
/// </remarks>
public static class GitOutputText
{
    /// <summary>
    /// Gösterilebilir satır sayısı üst sınırı; aşan çıktının <b>sonu</b> tutulur.
    /// </summary>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> 60.000 satır yazan bir hook, 4,1 MB'lık bir <c>stderr</c> üretti ve
    /// tamamı sorunsuz yakalandı (167 ms, kilitlenme yok). Yakalamak sorun değil;
    /// <b>göstermek</b> sorun — bir metin kutusuna 4 MB koymak arayüzü dondurur.
    /// Sonun tutulmasının sebebi: hook'lar özeti ve asıl hata satırını sona yazıyor.
    /// </remarks>
    public const int MaximumDisplayLines = 1000;

    /// <summary>
    /// Ham çıktıyı gösterime hazırlar ve <see cref="MaximumDisplayLines"/> sınırını aşarsa
    /// <b>sonunu</b> tutup atılan satır sayısını bildirir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ANSI dizileri siliniyor, <c>\r</c> üzerine yazma olarak uygulanıyor, satır sonlarındaki
    /// boşluk kırpılıyor. Sondaki boş satırlar atılıyor; baştakiler <b>korunuyor</b>
    /// (hook'un kendi biçimi olabilir).
    /// </para>
    /// <para>
    /// ⚠️ <b>Kırpma temizlemeden ÖNCE yapılıyor</b> ve bu bir mikro-optimizasyon değil:
    /// ölçüldü, 60.000 satırlık (4 MB) bir hook çıktısında önce temizleyip sonra kırpmak
    /// <b>60 ms ve 59,6 MB</b> tüketiyordu — üstelik hata gösterilirken <b>UI iş parçacığında</b>.
    /// Önce kırpınca aynı sonuç <b>2,1 ms ve 1,0 MB</b>'a düşüyor; %98'i zaten atılacak
    /// satırlar için harcanıyormuş.
    /// </para>
    /// </remarks>
    /// <param name="rawOutput">Ham <c>stderr</c> metni.</param>
    /// <param name="droppedLines">Atılan satır sayısı; kırpılmadıysa 0.</param>
    public static string CleanForDisplay(string? rawOutput, out int droppedLines)
    {
        droppedLines = 0;

        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> tail = TakeLastLines(rawOutput, MaximumDisplayLines, out droppedLines);

        return CleanLines(tail);
    }

    /// <summary>
    /// Metnin son <paramref name="maximumLines"/> satırını, kopya üretmeden döndürür.
    /// </summary>
    /// <remarks>
    /// Geriye doğru satır sonu sayan tek geçiş. Satır <b>nesnesi</b> üretilmiyor — asıl
    /// maliyet buydu.
    /// </remarks>
    private static ReadOnlySpan<char> TakeLastLines(
        string text,
        int maximumLines,
        out int droppedLines)
    {
        droppedLines = 0;

        int seen = 0;

        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] != '\n')
            {
                continue;
            }

            // Sondaki satır sonu yapay bir boş satır üretir; onu saymıyoruz.
            if (i == text.Length - 1)
            {
                continue;
            }

            seen++;

            if (seen == maximumLines)
            {
                droppedLines = CountLines(text.AsSpan(0, i));
                return text.AsSpan(i + 1);
            }
        }

        return text.AsSpan();
    }

    private static int CountLines(ReadOnlySpan<char> text)
    {
        int lines = 1;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        return lines;
    }

    /// <summary>
    /// Satırları temizler ve sondaki boş satırları atarak birleştirir.
    /// </summary>
    /// <remarks>
    /// <c>\r\n</c> için ayrıca dönüşüm yapılmıyor: <see cref="CleanLine"/> zaten <c>\r</c>'yi
    /// imleç sıfırlaması sayıyor, dolayısıyla satır sonundaki <c>\r</c> kendiliğinden
    /// düşüyor. Ölçüldü — 4 MB'lık CRLF çıktıda tam bir dize kopyası daha demekti.
    /// </remarks>
    private static string CleanLines(ReadOnlySpan<char> text)
    {
        List<string> lines = [];

        while (true)
        {
            int newline = text.IndexOf('\n');

            if (newline < 0)
            {
                lines.Add(CleanLine(text));
                break;
            }

            lines.Add(CleanLine(text[..newline]));
            text = text[(newline + 1)..];
        }

        // Sondaki boş satırlar: git ve hook'lar sona ayraç satırı bırakıyor.
        int end = lines.Count;
        while (end > 0 && lines[end - 1].Length == 0)
        {
            end--;
        }

        return string.Join('\n', lines.ToArray(), 0, end);
    }

    /// <summary>
    /// Tek satırdan ANSI kaçış dizilerini ve <c>\r</c> üzerine yazmayı temizler.
    /// </summary>
    private static string CleanLine(ReadOnlySpan<char> line)
    {
        StringBuilder buffer = new(line.Length);
        int cursor = 0;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '\u001b')
            {
                i = SkipEscape(line, i);
                continue;
            }

            if (c == '\r')
            {
                // Terminal davranışı: imleç satır başına döner, sonraki karakterler
                // öncekilerin ÜZERİNE yazar. Sonu kırpmak yanlış olurdu — "bitti" yazan
                // bir ilerleme satırı tamamen kaybolurdu.
                cursor = 0;
                continue;
            }

            if (c == '\b')
            {
                cursor = Math.Max(0, cursor - 1);
                continue;
            }

            // Diğer C0 kontrol karakterleri metin kutusunda çöp gösterir; sekme korunur.
            if (char.IsControl(c) && c != '\t')
            {
                continue;
            }

            if (cursor < buffer.Length)
            {
                buffer[cursor] = c;
            }
            else
            {
                buffer.Append(c);
            }

            cursor++;
        }

        return buffer.ToString().TrimEnd();
    }

    /// <summary>
    /// <paramref name="start"/> konumundaki ESC dizisinin <b>son</b> karakterinin indeksini döndürür.
    /// </summary>
    private static int SkipEscape(ReadOnlySpan<char> line, int start)
    {
        int i = start + 1;

        if (i >= line.Length)
        {
            return start;
        }

        char introducer = line[i];

        if (introducer == '[')
        {
            // CSI: ESC [ parametreler son-bayt(@-~). Renk kodları (SGR) bu gruptadır.
            i++;
            while (i < line.Length && line[i] is >= ' ' and <= '?')
            {
                i++;
            }

            return i < line.Length ? i : line.Length - 1;
        }

        if (introducer is ']' or 'P' or 'X' or '^' or '_')
        {
            // OSC ve arkadaşları: BEL veya ESC \ ile biter. Satır içinde bitmezse satırın
            // tamamı yutulur — kalanı göstermek yarım bir kaçış dizisi göstermek olurdu.
            i++;
            while (i < line.Length)
            {
                if (line[i] == '\u0007')
                {
                    return i;
                }

                if (line[i] == '\u001b' && i + 1 < line.Length && line[i + 1] == '\\')
                {
                    return i + 1;
                }

                i++;
            }

            return line.Length - 1;
        }

        // İki karakterlik basit kaçış (ESC c gibi).
        return i;
    }
}
