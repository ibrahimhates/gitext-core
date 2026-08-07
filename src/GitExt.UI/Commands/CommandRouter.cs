using System.Windows.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// Bir komut kimliğini onu gerçekten çalıştıracak yere iletir (P08-T04).
/// </summary>
/// <remarks>
/// <para>
/// Kısayol dağıtımı ile komut paleti <b>aynı</b> yürütme yolunu kullanmak zorunda. Ayrı
/// olsalardı bir komut palette çalışıp kısayolla çalışmayabilir — ya da tersi — olurdu ve
/// hangisinin doğru olduğunu kimse bilemezdi.
/// </para>
/// <para>
/// İki kaynak var: küresel komutlar doğrudan bir <see cref="ICommand"/>, panel komutları ise
/// o panelin <see cref="ShortcutDispatcher"/>'ında. Ayrım tesadüf değil — panel komutları
/// görünüme ait durumu (seçili satırlar, odak) okuyor ve o durum yalnızca panelde var.
/// </para>
/// </remarks>
public sealed class CommandRouter
{
    private readonly Dictionary<string, ICommand> _global = new(StringComparer.Ordinal);
    private readonly List<ShortcutDispatcher> _panels = [];

    public void Register(string commandId, ICommand command) => _global[commandId] = command;

    public void Register(ShortcutDispatcher dispatcher)
    {
        if (!_panels.Contains(dispatcher))
        {
            _panels.Add(dispatcher);
        }
    }

    public IReadOnlyDictionary<string, ICommand> GlobalCommands => _global;

    /// <summary>
    /// Komut şu anda çalıştırılabilir mi?
    /// </summary>
    /// <remarks>
    /// Panel komutları için "bağlı mı" sorusuna bakılıyor, "şu an anlamlı mı" sorusuna değil:
    /// ikincisi ancak çalıştırılınca belli oluyor (seçim yoksa işleyici <c>false</c> döner).
    /// </remarks>
    public bool CanRun(string commandId) =>
        (_global.TryGetValue(commandId, out ICommand? command) && command.CanExecute(null))
        || _panels.Any(p => p.BoundCommands.Contains(commandId));

    /// <summary>Komutu çalıştırır.</summary>
    /// <returns>Çalıştırıldıysa <see langword="true"/>.</returns>
    public bool Run(string commandId)
    {
        if (_global.TryGetValue(commandId, out ICommand? command))
        {
            if (!command.CanExecute(null))
            {
                return false;
            }

            command.Execute(null);

            return true;
        }

        foreach (ShortcutDispatcher panel in _panels)
        {
            if (panel.TryInvoke(commandId))
            {
                return true;
            }
        }

        return false;
    }
}
