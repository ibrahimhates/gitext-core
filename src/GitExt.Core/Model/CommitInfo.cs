namespace GitExt.Core.Model;

/// <summary>
/// Bir commit'in listelemede ve detay panelinde gösterilen bilgileri.
/// </summary>
/// <remarks>
/// Alanlar <c>git log</c>'un tek geçişte döndürebildiği şeylerle sınırlıdır; her alan için
/// ek bir süreç çağrısı yapmak, büyük depolarda kabul edilemez maliyet üretir (ADR-0002).
/// </remarks>
public sealed record CommitInfo
{
    /// <summary>Tam commit kimliği.</summary>
    public required CommitId Id { get; init; }

    /// <summary>
    /// Ebeveyn commit'ler.
    /// </summary>
    /// <remarks>
    /// Kök commit'te boştur; merge'de birden fazladır. Octopus merge 2'den fazla ebeveyn
    /// içerebilir — bu liste bilinçli olarak sınırsızdır.
    /// </remarks>
    public required IReadOnlyList<CommitId> Parents { get; init; }

    /// <summary>Değişikliği yazan kişi.</summary>
    public required Signature Author { get; init; }

    /// <summary>
    /// Commit'i depoya kaydeden kişi.
    /// </summary>
    /// <remarks>
    /// Rebase, cherry-pick ve yama uygulama sonrasında yazardan farklı olur; ayrıca
    /// tarihi de farklıdır. Grafikte hangisinin gösterileceği bir UI kararıdır.
    /// </remarks>
    public required Signature Committer { get; init; }

    /// <summary>Mesajın ilk satırı.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Mesajın kalan gövdesi. Yalnızca başlıktan oluşan commit'lerde boştur.
    /// </summary>
    public required string Body { get; init; }

    /// <summary>
    /// Bu commit'e işaret eden ref'ler (dal, tag, <c>HEAD</c>).
    /// </summary>
    public IReadOnlyList<string> Refs { get; init; } = [];

    /// <summary>
    /// Commit nesnesinde kayıtlı kodlama; UTF-8 ise boş.
    /// </summary>
    /// <remarks>
    /// Yalnızca bilgi amaçlıdır. <see cref="Subject"/> ve <see cref="Body"/> her zaman
    /// UTF-8'e çevrilmiş olarak gelir — bunu <c>i18n.logOutputEncoding=UTF-8</c> garanti eder.
    /// </remarks>
    public string Encoding { get; init; } = string.Empty;

    /// <summary>Birden fazla ebeveyni olan commit mi?</summary>
    public bool IsMerge => Parents.Count > 1;

    /// <summary>Ebeveyni olmayan commit mi (geçmişin kökü)?</summary>
    public bool IsRoot => Parents.Count == 0;

    /// <summary>
    /// Başlık ve gövdeyi birleştiren tam mesaj.
    /// </summary>
    public string FullMessage =>
        string.IsNullOrEmpty(Body) ? Subject : $"{Subject}\n\n{Body}";

    public override string ToString() => $"{Id.ToShortString()} {Subject}";
}
