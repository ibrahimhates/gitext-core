using GitExt.UI.ViewModels;

namespace GitExt.UI.Tests.ViewModels;

/// <summary>
/// P01-T17 — İlk gerçek test.
/// Küçük ama asıl amacı test altyapısının (xunit v3 + Shouldly + MVVM source generator)
/// uçtan uca çalıştığını kanıtlamaktır.
/// </summary>
public class MainWindowViewModelTests
{
    [Fact]
    public void Greeting_varsayilan_degeriyle_baslar()
    {
        MainWindowViewModel sut = new();

        sut.Greeting.ShouldBe("Hello World");
    }

    [Fact]
    public void Greeting_degistiginde_PropertyChanged_tetiklenir()
    {
        // CommunityToolkit.Mvvm source generator'ının gerçekten çalıştığını doğrular (ADR-0004).
        MainWindowViewModel sut = new();
        List<string?> changed = [];
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sut.Greeting = "Merhaba";

        sut.Greeting.ShouldBe("Merhaba");
        changed.ShouldContain(nameof(MainWindowViewModel.Greeting));
    }
}
