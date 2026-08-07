using Avalonia.Controls;

namespace GitExt.UI.Commands;

/// <summary>
/// Paneller arası odak gezinmesi (P08-T05).
/// </summary>
/// <remarks>
/// <para>
/// <c>Tab</c> tek tek denetimler arasında dolaşır; büyük bir pencerede bir panelden diğerine
/// geçmek onlarca <c>Tab</c> demek. <c>F6</c> panel atlar — P08-T00/M09'da ölçüldü:
/// Avalonia'da <c>F6</c> varsayılan olarak <b>hiçbir şey yapmıyor</b>, yani serbest.
/// </para>
/// <para>
/// 🔴 <b>Görünmeyen paneller atlanıyor.</b> Gizli bir panele odak vermek, kullanıcının
/// odağı <b>kaybetmesi</b> demektir: tuşlar hiçbir yere gitmez ve ekranda hiçbir şey
/// değişmez — klavye sessizce ölür (Faz 03'te ölçülen tuzağın aynısı).
/// </para>
/// </remarks>
public sealed class PanelNavigator
{
    private readonly List<Panel> _panels = [];

    private sealed record Panel(string Id, Func<bool> IsAvailable, Func<bool> Focus);

    /// <summary>Sıraya bir panel ekler. Ekleme sırası gezinme sırasıdır.</summary>
    public PanelNavigator Add(string id, Func<bool> isAvailable, Func<bool> focus)
    {
        _panels.Add(new Panel(id, isAvailable, focus));

        return this;
    }

    /// <summary>Şu an odağı taşıyan panelin sırası; hiçbiri değilse <c>-1</c>.</summary>
    public int CurrentIndex(Func<string, bool> hasFocus) =>
        _panels.FindIndex(p => hasFocus(p.Id));

    /// <summary>Belirli bir panele odaklanır.</summary>
    public bool FocusPanel(string id)
    {
        Panel? panel = _panels.Find(p => p.Id == id);

        return panel is not null && panel.IsAvailable() && panel.Focus();
    }

    /// <summary>
    /// Odağı sıradaki kullanılabilir panele taşır.
    /// </summary>
    /// <param name="hasFocus">Verilen panelin şu an odağı taşıyıp taşımadığı.</param>
    /// <param name="delta">İleri için <c>1</c>, geri için <c>-1</c>.</param>
    public bool Move(Func<string, bool> hasFocus, int delta)
    {
        if (_panels.Count == 0)
        {
            return false;
        }

        int start = CurrentIndex(hasFocus);

        // Odak hiçbir panelde değilse (menüde, şeritte…) ilk panele gidiliyor: kullanıcı
        // F6'ya bastıysa "bir panele geç" demek istiyor, "hiçbir yerde kal" değil.
        if (start < 0)
        {
            start = delta > 0 ? -1 : 0;
        }

        for (int step = 1; step <= _panels.Count; step++)
        {
            int index = (start + (delta * step)) % _panels.Count;

            if (index < 0)
            {
                index += _panels.Count;
            }

            Panel candidate = _panels[index];

            if (candidate.IsAvailable() && candidate.Focus())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bir denetimin ya da alt ağacının odağı taşıyıp taşımadığı.
    /// </summary>
    /// <remarks>
    /// <c>IsFocused</c> yetmiyor: odak neredeyse her zaman panelin <b>içindeki</b> bir
    /// öğede (bir <c>ListBoxItem</c>, bir <c>TextBox</c>). <c>:focus-within</c> ile aynı soru.
    /// </remarks>
    public static bool ContainsFocus(Control? control) =>
        control is not null && control.IsKeyboardFocusWithin;
}
