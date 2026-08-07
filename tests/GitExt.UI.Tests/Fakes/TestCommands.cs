using GitExt.UI.Commands;

namespace GitExt.UI.Tests;

/// <summary>
/// Testlerin kullandığı komut kaydı.
/// </summary>
/// <remarks>
/// <b>Gerçek şemayı</b> kullanıyor, sahte bir şemayı değil: kısayol testlerinin değeri tam
/// olarak "kullanıcının basacağı tuş doğru işi yapıyor mu" sorusunda; sahte bir şemayla
/// yapılsalardı yalnızca dağıtım mekanizmasını sınarlardı.
/// </remarks>
public static class TestCommands
{
    public static CommandRegistry Registry() =>
        new(new InMemorySettingsStore(), DefaultCommandScheme.Definitions);
}
