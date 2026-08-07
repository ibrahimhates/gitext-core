using Avalonia.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// Bir panelin bağlam kısayollarını dağıtır (P08-T01).
/// </summary>
/// <remarks>
/// <para>
/// Görünümler tuş olayını kendileri okumaz; jesti buraya verir, karşılığında komut çalışır.
/// Böylece "hangi tuş neyi yapıyor" sorusunun tek bir cevabı olur:
/// <see cref="ICommandRegistry"/>.
/// </para>
/// <para>
/// <b>Neden pencere bağlaması değil?</b> P08-T00/M11: <c>Window.KeyBindings</c>'e konan bir jest
/// odaklı kontrolden tuşu koşulsuz çalar. Panel kısayolları (çıplak <c>S</c>, <c>PgDn</c>,
/// <c>Delete</c>…) oraya konsaydı, listelerin kendi gezinmesi ve metin kutuları çalışmaz
/// hâle gelirdi. Bu yüzden panel jestleri panelin <b>kendi tünelleyen işleyicisinden</b>
/// dağıtılıyor — mevcut kodun zaten yaptığı şey, artık merkezî bir kaynakla.
/// </para>
/// </remarks>
public sealed class ShortcutDispatcher
{
    private readonly ICommandRegistry _registry;
    private readonly CommandContext _context;
    private readonly Dictionary<string, Func<bool>> _handlers = new(StringComparer.Ordinal);

    public ShortcutDispatcher(ICommandRegistry registry, CommandContext context)
    {
        _registry = registry;
        _context = context;
    }

    /// <summary>
    /// Komutu bir işleyiciye bağlar.
    /// </summary>
    /// <param name="commandId">Bağlanacak komutun kimliği.</param>
    /// <param name="handler">
    /// Olayın <b>tüketilip tüketilmediğini</b> döndürür. <see langword="false"/> dönmek
    /// "bu tuşu ben işlemedim, yoluna devam etsin" demektir — dosyanın sonunda
    /// <c>Alt+↓</c>'yi yutmak kullanıcıya sessiz bir duvar olurdu.
    /// </param>
    public void Bind(string commandId, Func<bool> handler)
    {
        // Tanımsız bir kimliğe bağlanmak sessiz bir hata olurdu: kısayol hiç çalışmaz,
        // kimse de sebebini göremez.
        if (_registry.Find(commandId) is null)
        {
            throw new ArgumentException($"Tanımsız komut kimliği: {commandId}", nameof(commandId));
        }

        _handlers[commandId] = handler;
    }

    /// <summary>Her zaman tüketen bir işleyici bağlar.</summary>
    public void Bind(string commandId, Action handler) =>
        Bind(commandId, () =>
        {
            handler();

            return true;
        });

    /// <summary>
    /// Tuş olayını çözer ve bağlıysa çalıştırır.
    /// </summary>
    /// <returns>Olay tüketildiyse <see langword="true"/>.</returns>
    public bool Handle(KeyEventArgs e)
    {
        if (e.Key is Key.None)
        {
            return false;
        }

        string? commandId = _registry.Resolve(new KeyGesture(e.Key, e.KeyModifiers), _context);

        if (commandId is null || !_handlers.TryGetValue(commandId, out Func<bool>? handler))
        {
            return false;
        }

        if (!handler())
        {
            return false;
        }

        e.Handled = true;

        return true;
    }

    /// <summary>Bu bağlamda gerçekten bir işleyicisi olan komutlar.</summary>
    public IReadOnlyCollection<string> BoundCommands => _handlers.Keys;
}
