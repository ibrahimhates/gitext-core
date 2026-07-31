using Avalonia.Controls;

namespace GitExt.UI.Views;

/// <summary>
/// Pencere açma yardımcıları.
/// </summary>
internal static class WindowExtensions
{
    /// <summary>
    /// Pencereyi <b>modeless</b> olarak, varsa sahibinin üstünde açar.
    /// </summary>
    /// <remarks>
    /// <c>Show()</c> — <c>ShowDialog</c> değil. Karşılaştırma penceresi (P04-T16) ve git
    /// çıktısı penceresi (P05-T07) aynı gerekçeyi paylaşıyor: kullanıcı içeriği açık tutup
    /// asıl pencerede çalışmaya devam edebilmeli. Sahip yoksa (headless testler) sahipsiz
    /// açılır.
    /// </remarks>
    internal static void ShowOwnedBy(this Window window, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (owner is null)
        {
            window.Show();
        }
        else
        {
            window.Show(owner);
        }
    }
}
