namespace GitExt.Core.Model;

/// <summary>
/// Bir commit'in yazarı veya kaydedeni (committer): kim, ne zaman.
/// </summary>
/// <param name="Name">Görünen ad. Boş olabilir — git bunu zorunlu tutmaz.</param>
/// <param name="Email">E-posta adresi. Boş olabilir.</param>
/// <param name="When">
/// Zaman damgası, <b>orijinal saat dilimi ofsetiyle birlikte</b>.
/// </param>
/// <remarks>
/// <see cref="DateTimeOffset"/> kullanılıyor çünkü commit'in hangi saat diliminde atıldığı
/// anlamlı bilgidir ve gösterilmek istenebilir. <see cref="DateTime"/>'a çevirmek bunu kaybeder.
/// </remarks>
public sealed record Signature(string Name, string Email, DateTimeOffset When)
{
    /// <summary>Yalnızca gösterim için: <c>Ad &lt;e-posta&gt;</c>.</summary>
    public override string ToString() =>
        string.IsNullOrEmpty(Email) ? Name : $"{Name} <{Email}>";
}
