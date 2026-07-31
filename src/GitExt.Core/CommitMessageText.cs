namespace GitExt.Core;

/// <summary>
/// Commit mesajı metni üzerinde git'in kendi kurallarıyla çalışan yardımcılar (P05-T13).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Bu sınıfın var olma sebebi ölçüldü.</b> Bizim commit yolumuz
/// <c>git commit -F - --cleanup=whitespace</c> (P05-T06) ve bu modda yorum satırları
/// <b>korunuyor</b> — bilinçli bir karardı, kullanıcının <c>#123</c> gibi issue referansları
/// kaybolmasın diye. Ama git'in <b>editör</b> yolu (<c>--cleanup=default</c>) yorum
/// satırlarını <b>siliyor</b>, ve <c>commit.template</c> ile <c>.git/MERGE_MSG</c>
/// tam olarak o yolun girdisi:
/// </para>
/// <code>
/// MERGE_MSG:                          git'in editörle ürettiği commit:
///   Merge branch 'dal'                  Merge branch 'dal'
///                                    ←  (yorumlar YOK)
///   # Conflicts:
///   #	a.txt
/// </code>
/// <para>
/// Yani bu dosyaları kutuya olduğu gibi yükleyip commit'leseydik, kullanıcı git'in kendisiyle
/// yaptığında <b>almayacağı</b> bir mesaj alırdı — commit gövdesinde <c># Conflicts:</c>
/// satırları. Yorumlar <b>yüklenirken</b> temizleniyor (kutuda görünen = commit'lenen);
/// kullanıcının kendi yazdığı metne asla dokunulmuyor.
/// </para>
/// </remarks>
public static class CommitMessageText
{
    /// <summary>git'in <c>core.commentChar</c> ayarlanmadığındaki varsayılanı.</summary>
    public const string DefaultCommentCharacter = "#";

    /// <summary>
    /// Yorum karakteri ayarını gerçek bir değere çevirir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ÖLÇÜLDÜ:</b> <c>core.commentChar</c> <c>#</c> olmak zorunda değil. <c>;</c> yapılınca
    /// git <c>;</c> ile başlayan satırları siliyor ve <c>#</c> ile başlayanları
    /// <b>koruyor</b> — kör bir <c>#</c> filtresi bu depoda hem gerçek yorumları bırakır hem
    /// de kullanıcının issue satırlarını siler. Değer <b>çok karakterli</b> de olabiliyor
    /// (git 2.45+; <c>//</c> kabul edildi).
    /// </para>
    /// <para>
    /// <c>auto</c> özel bir değer (git 2.55'te <i>deprecated</i>, git 3.0'da kalkıyor): git
    /// mesajda kullanılmayan bir karakter seçiyor, yani sabit bir cevabı yok. Bu durumda
    /// varsayılana dönülüyor — yanlış tahminle kullanıcının satırını silmektense yorumu
    /// bırakmak yeğdir.
    /// </para>
    /// </remarks>
    public static string ResolveCommentCharacter(string? configuredValue) =>
        configuredValue switch
        {
            null or "" => DefaultCommentCharacter,
            "auto" => DefaultCommentCharacter,
            _ => configuredValue,
        };

    /// <summary>
    /// Yorum satırlarını siler — git'in <c>--cleanup=default</c> yolunun yaptığının aynısı.
    /// </summary>
    /// <param name="text">Şablon veya <c>MERGE_MSG</c> içeriği.</param>
    /// <param name="commentCharacter">Yorum ön eki; boşsa varsayılan kullanılır.</param>
    /// <remarks>
    /// <b>ÖLÇÜLDÜ:</b> yalnızca <b>satır başındaki</b> ön ek yorum sayılıyor —
    /// <c>␣␣# girintili</c> satırı git'in kendisi de <b>silmiyor</b>. <c>TrimStart</c>
    /// eklemek, bir kod parçasını içeren şablonda gerçek metni silmek olurdu.
    /// </remarks>
    public static string RemoveComments(string text, string? commentCharacter = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        string prefix = ResolveCommentCharacter(commentCharacter);

        if (text.Length == 0)
        {
            return text;
        }

        // Satır sonu biçimi korunuyor: dosya CRLF ise CRLF kalır. Yamalarda olduğu gibi
        // (P04-T07) burada da satır sonlarını normalleştirmek bizim işimiz değil.
        string[] lines = text.Split('\n');

        IEnumerable<string> kept = lines.Where(line =>
            !line.StartsWith(prefix, StringComparison.Ordinal));

        return string.Join('\n', kept);
    }

    /// <summary>
    /// Kutuya yüklenecek metni hazırlar: yorumlar silinir, baştaki/sondaki boş satırlar atılır.
    /// </summary>
    /// <remarks>
    /// Yorumlar gidince geriye çoğu zaman bir sürü boş satır kalıyor (<c>MERGE_MSG</c>'de
    /// konu satırından sonra iki boş satır ve dosya sonu). Kutuda imlecin metnin
    /// <b>ortasında</b> başlaması kullanıcıya "burada bir şey vardı" hissi verirdi.
    /// </remarks>
    public static string PrepareForEditing(string text, string? commentCharacter = null) =>
        RemoveComments(text, commentCharacter).Trim('\n', '\r', ' ', '\t');
}
