using System.Text;
using GitExt.Core.Model;

namespace GitExt.Core;

/// <summary>
/// <c>.gitignore</c> satırı üretir (P05-T08).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>ÖLÇÜLDÜ:</b> dosya adını <b>ham</b> yazmak sessizce çalışmıyor. <c>#</c> ile başlayan
/// ad yorum, <c>!</c> ile başlayan ad olumsuzlama, <c>[</c> içeren ad karakter sınıfı,
/// <c>\</c> içeren ad kaçış sayılıyor — dördünde de git dosyayı <b>yok saymıyor</b> ama bir
/// hata da vermiyor. Kullanıcı "yok say" diyor, uygulama "tamam" diyor, dosya listede
/// kalmaya devam ediyor.
/// </para>
/// <para>
/// Üretilen desen <b>köke sabitlenir</b> (baştaki <c>/</c>): sabitlenmezse desen depodaki
/// <b>aynı adlı her dosyaya</b> uyar — kullanıcı bir tanesini seçmişken.
/// </para>
/// </remarks>
public static class GitIgnorePattern
{
    /// <summary>
    /// Verilen yolu <b>yalnızca o yolu</b> yok sayan bir desene çevirir.
    /// </summary>
    public static string ForPath(RepositoryPath path) => "/" + Escape(path.Value);

    /// <summary>
    /// Verilen yolun bulunduğu dizini yok sayan desen üretir; yol köke aitse
    /// <see langword="null"/>.
    /// </summary>
    public static string? ForDirectoryOf(RepositoryPath path)
    {
        int separator = path.Value.LastIndexOf('/');

        return separator <= 0 ? null : "/" + Escape(path.Value[..separator]) + "/";
    }

    /// <summary>
    /// Aynı uzantıya sahip <b>tüm</b> dosyaları yok sayan desen üretir; uzantı yoksa
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Bu desen bilinçli olarak <b>sabitlenmez</b>: "tüm <c>.log</c> dosyaları" isteğinin
    /// tanımı gereği her dizinde geçerli olması gerekiyor.
    /// </remarks>
    public static string? ForExtensionOf(RepositoryPath path)
    {
        string name = path.Value[(path.Value.LastIndexOf('/') + 1)..];

        // Baştaki nokta uzantı değil, gizli dosya adıdır (`.env` → uzantı yok).
        int dot = name.LastIndexOf('.');

        return dot <= 0 || dot == name.Length - 1 ? null : "*" + Escape(name[dot..]);
    }

    /// <summary>
    /// Bir yolu <c>.gitignore</c> deseni içinde <b>birebir</b> eşleşecek hale getirir.
    /// </summary>
    /// <remarks>
    /// Kaçırılanlar ve sebepleri (hepsi ölçüldü): <c>\</c> kaçış karakteri · <c>*</c> ve
    /// <c>?</c> joker · <c>[</c> karakter sınıfı başlangıcı · satır başındaki <c>#</c> yorum ·
    /// satır başındaki <c>!</c> olumsuzlama.
    /// <para>
    /// Boşluk kaçırılmıyor: ölçüldü, <b>satır içi</b> boşluk sorun değil. Yalnızca satır
    /// <b>sonundaki</b> boşluk git tarafından kırpılıyor; o durumda son karakter kaçırılıyor.
    /// </para>
    /// </remarks>
    public static string Escape(string value)
    {
        StringBuilder builder = new(value.Length + 4);

        foreach (char c in value)
        {
            if (c is '\\' or '*' or '?' or '[')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        // `#` ve `!` yalnızca satır BAŞINDA özel; desen `/` ile başladığında zaten
        // sorun olmaz ama sabitlenmemiş desenler (uzantı) için gerekli.
        if (builder.Length > 0 && builder[0] is '#' or '!')
        {
            builder.Insert(0, '\\');
        }

        // Sondaki boşluk git tarafından kırpılır; kaçırılmazsa desen adın son karakterini
        // kaybeder ve hiçbir şeye uymaz.
        if (builder.Length > 0 && builder[^1] == ' ')
        {
            builder.Insert(builder.Length - 1, '\\');
        }

        return builder.ToString();
    }
}
