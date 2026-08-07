using GitExt.UI.Commands;

namespace GitExt.UI.Tests.Commands;

/// <summary>
/// P08-T05 — panel gezinmesi.
/// </summary>
public class PanelNavigatorTests
{
    private sealed class Fake
    {
        public readonly List<string> Focused = [];
        public readonly HashSet<string> Hidden = [];
        public string? Current;

        public PanelNavigator Build(params string[] ids)
        {
            PanelNavigator navigator = new();

            foreach (string id in ids)
            {
                string captured = id;

                navigator.Add(
                    captured,
                    () => !Hidden.Contains(captured),
                    () =>
                    {
                        Focused.Add(captured);
                        Current = captured;

                        return true;
                    });
            }

            return navigator;
        }

        public bool HasFocus(string id) => Current == id;
    }

    [Fact]
    public void Ileri_gezinme_sirayla_ilerliyor()
    {
        Fake fake = new();
        PanelNavigator navigator = fake.Build("a", "b", "c");
        fake.Current = "a";

        navigator.Move(fake.HasFocus, 1).ShouldBeTrue();
        fake.Current.ShouldBe("b");

        navigator.Move(fake.HasFocus, 1);
        fake.Current.ShouldBe("c");
    }

    [Fact]
    public void Sondan_sonra_basa_donuyor()
    {
        Fake fake = new();
        PanelNavigator navigator = fake.Build("a", "b", "c");
        fake.Current = "c";

        navigator.Move(fake.HasFocus, 1);

        fake.Current.ShouldBe("a");
    }

    [Fact]
    public void Geri_gezinme_ters_yonde()
    {
        Fake fake = new();
        PanelNavigator navigator = fake.Build("a", "b", "c");
        fake.Current = "a";

        navigator.Move(fake.HasFocus, -1);

        fake.Current.ShouldBe("c");
    }

    /// <summary>
    /// 🔴 Görünmeyen panel <b>atlanıyor</b>.
    /// </summary>
    /// <remarks>
    /// Gizli bir panele odak vermek, kullanıcının odağı kaybetmesi demektir: tuşlar hiçbir
    /// yere gitmez ve ekranda hiçbir şey değişmez. Belirti "F6 çalışmıyor" olur ve sebebi
    /// hiçbir yerde görünmez.
    /// </remarks>
    [Fact]
    public void Gorunmeyen_panel_atlaniyor()
    {
        Fake fake = new();
        PanelNavigator navigator = fake.Build("a", "b", "c");
        fake.Hidden.Add("b");
        fake.Current = "a";

        navigator.Move(fake.HasFocus, 1);

        fake.Current.ShouldBe("c");
    }

    /// <summary>Odak hiçbir panelde değilse ilk kullanılabilir panele gidiliyor.</summary>
    [Fact]
    public void Odak_disaridayken_ilk_panele_gidiliyor()
    {
        Fake fake = new();
        PanelNavigator navigator = fake.Build("a", "b", "c");
        fake.Current = null;

        navigator.Move(fake.HasFocus, 1);

        fake.Current.ShouldBe("a");
    }

    [Fact]
    public void Hicbir_panel_kullanilabilir_degilse_hareket_yok()
    {
        Fake fake = new();
        PanelNavigator navigator = fake.Build("a", "b");
        fake.Hidden.Add("a");
        fake.Hidden.Add("b");

        navigator.Move(fake.HasFocus, 1).ShouldBeFalse();
        fake.Focused.ShouldBeEmpty();
    }

    [Fact]
    public void Dogrudan_odaklanma_calisiyor()
    {
        Fake fake = new();
        PanelNavigator navigator = fake.Build("a", "b");

        navigator.FocusPanel("b").ShouldBeTrue();
        fake.Current.ShouldBe("b");
    }

    [Fact]
    public void Gizli_panele_dogrudan_odaklanilamiyor()
    {
        Fake fake = new();
        PanelNavigator navigator = fake.Build("a", "b");
        fake.Hidden.Add("b");

        navigator.FocusPanel("b").ShouldBeFalse();
        fake.Focused.ShouldBeEmpty();
    }
}
