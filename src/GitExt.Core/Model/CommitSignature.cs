namespace GitExt.Core.Model;

/// <summary>
/// Bir commit imzasının doğrulama sonucu (P03-T15).
/// </summary>
/// <remarks>
/// <c>git log --format=%G?</c> alanının karşılıkları. Bir tanesi doğrudan git'ten gelmiyor:
/// <see cref="CannotVerify"/>, git'in doğrulayıcıyı hiç çalıştıramadığı durumu temsil eder —
/// bkz. <see cref="CommitSignatureInfo"/>.
/// </remarks>
public enum SignatureStatus
{
    /// <summary>İmza yok (<c>N</c>).</summary>
    None,

    /// <summary>Geçerli imza (<c>G</c>).</summary>
    Valid,

    /// <summary>İmza geçerli ama anahtar güvenilir işaretlenmemiş (<c>U</c>).</summary>
    ValidUntrusted,

    /// <summary>İmza <b>hatalı</b> (<c>B</c>) — içerik imzalandıktan sonra değişmiş olabilir.</summary>
    Bad,

    /// <summary>İmzanın süresi dolmuş (<c>X</c>).</summary>
    Expired,

    /// <summary>İmzalayan anahtarın süresi dolmuş (<c>Y</c>).</summary>
    KeyExpired,

    /// <summary>İmzalayan anahtar iptal edilmiş (<c>R</c>).</summary>
    KeyRevoked,

    /// <summary>Doğrulama yapılamadı (<c>E</c> veya yapılandırma eksik).</summary>
    CannotVerify,
}

/// <summary>
/// Bir commit'in imza bilgisi.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neden toplu <c>git log</c> okumasında yok?</b> Ölçüldü: 2.000 <b>imzasız</b> commit'te
/// formata <c>%G?</c> eklemek okumayı 12,1 ms'den 20,8 ms'ye çıkarıyor (%72). Gerçekten imzalı
/// commit'lerde her satır için kriptografik doğrulama yapılacağı için fark çok daha büyür.
/// Detay paneli tek seferde tek commit gösterdiğinden imza <b>seçili commit için ayrıca</b>
/// okunuyor.
/// </para>
/// <para>
/// <b>⚠️ <c>%G?</c> tek başına yanıltıcı.</b> Ölçüldü: SSH imzalı bir commit,
/// <c>gpg.ssh.allowedSignersFile</c> yapılandırılmamışsa <c>N</c> — yani "imzasız" — döner;
/// git yalnızca stderr'e hata yazar. Kullanıcıya imzalı bir commit için "imzasız" demek
/// yanlış bilgidir, bu yüzden stderr'e de bakılıp <see cref="SignatureStatus.CannotVerify"/>
/// ayrımı yapılır.
/// </para>
/// </remarks>
public sealed record CommitSignatureInfo
{
    /// <summary>İmzası olmayan commit için hazır değer.</summary>
    public static CommitSignatureInfo Unsigned { get; } = new() { Status = SignatureStatus.None };

    public required SignatureStatus Status { get; init; }

    /// <summary>İmzalayanın adı/e-postası (<c>%GS</c>); bilinmiyorsa boş.</summary>
    public string Signer { get; init; } = string.Empty;

    /// <summary>İmzalayan anahtarın kimliği (<c>%GK</c>); bilinmiyorsa boş.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Doğrulama neden yapılamadı? Yalnızca <see cref="SignatureStatus.CannotVerify"/>
    /// durumunda dolu.
    /// </summary>
    public string? CannotVerifyReason { get; init; }

    /// <summary>Commit imzalı mı? Doğrulanamayan imza da imzadır.</summary>
    public bool IsSigned => Status != SignatureStatus.None;

    /// <summary>
    /// İmza güvenilir sayılabilir mi?
    /// </summary>
    /// <remarks>
    /// Yalnızca <see cref="SignatureStatus.Valid"/> için <see langword="true"/>. Güven
    /// işaretlenmemiş (<c>U</c>) imza "doğrulandı" diye sunulmamalı.
    /// </remarks>
    public bool IsTrusted => Status == SignatureStatus.Valid;

    public override string ToString() => Status.ToString();
}
