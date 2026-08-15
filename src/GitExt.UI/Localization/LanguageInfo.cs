namespace GitExt.UI.Localization;

/// <summary>
/// Kullanılabilir bir arayüz dili (P11-T01).
/// </summary>
/// <param name="Code">Dosya adından gelen dil kodu: <c>en</c>, <c>tr</c>.</param>
/// <param name="Name">
/// Dilin <b>kendi dilindeki</b> adı: <c>English</c>, <c>Türkçe</c>.
/// </param>
/// <remarks>
/// Ad, dil dosyasının <c>_meta.name</c> alanından okunuyor; koda gömülü bir tablodan değil.
/// Sebebi: yeni bir dil eklemek <b>yalnızca</b> bir JSON dosyası eklemek olmalı. Adlar kodda
/// tutulsaydı, <c>fr.json</c> ekleyen biri listede "fr" görür ve adı yazmak için C# dosyasına
/// dokunmak zorunda kalırdı.
/// <para>
/// Ad neden İngilizce değil kendi dilinde: dil seçicide "Turkish" yazması, o dili arayan
/// kullanıcının anlamadığı bir dilde yazılmış demektir. Dil listesi, o dili bilmeyen birinin
/// değil, arayan birinin okuyabilmesi için var.
/// </para>
/// </remarks>
public sealed record LanguageInfo(string Code, string Name)
{
    /// <summary>Açılır listede gösterilen metin.</summary>
    public override string ToString() => Name;
}
