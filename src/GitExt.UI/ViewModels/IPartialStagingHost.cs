using GitExt.Core;
using GitExt.Core.Model;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Diff panelindeki kısmi staging eylemlerini gerçekleştiren taraf (P05-T10).
/// </summary>
/// <remarks>
/// <see cref="DiffViewModel"/> bilinçli olarak <b>bağımsız</b> (P04-T08): commit geçmişinde
/// ve karşılaştırma penceresinde staging anlamsız. Bu arayüz, staging'in anlamlı olduğu tek
/// yerde (çalışma dizini görünümü) dışarıdan takılıyor.
/// </remarks>
public interface IPartialStagingHost
{
    /// <summary>Gösterilen taraf stage'lenebilir mi (çalışma ağacı tarafı)?</summary>
    bool CanStage { get; }

    /// <summary>Gösterilen taraf geri alınabilir mi (index tarafı)?</summary>
    bool CanUnstage { get; }

    /// <summary>
    /// Seçimi uygular ve listeleri tazeler.
    /// </summary>
    /// <param name="diff">Seçimin ait olduğu dosya farkı.</param>
    /// <param name="selection">Uygulanacak satır seçimi.</param>
    /// <param name="stage">
    /// <see langword="true"/> ise stage'ler, aksi halde index'ten geri alır.
    /// </param>
    Task ApplyAsync(FileDiff diff, PatchSelection selection, bool stage);

    /// <summary>
    /// Seçili satırlardaki değişiklikleri <b>çalışma ağacından atar</b> (P05-T15).
    /// </summary>
    /// <remarks>
    /// Stage/unstage'den ayrı bir metot: bu <b>yıkıcı</b> bir işlem, onay istiyor ve
    /// yedekleniyor. Aynı çağrıya üçüncü bir bayrak olarak eklenseydi, çağıranın onu
    /// yanlışlıkla geçmesi mümkün olurdu.
    /// </remarks>
    Task DiscardAsync(FileDiff diff, PatchSelection selection);
}
