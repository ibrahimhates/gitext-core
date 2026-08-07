using Avalonia.Input;
using GitExt.UI.Settings;

namespace GitExt.UI.Commands;

/// <summary>
/// Uygulamadaki bütün komutların ve kısayollarının <b>tek kayıt yeri</b> (P08-T01).
/// </summary>
/// <remarks>
/// Kısayolları XAML'e ve kod-arkası <c>switch</c>'lere dağıtmak sürdürülemez: Faz 08'e
/// girilirken jestler <b>altı ayrı dosyada</b>, üstelik iki farklı mekanizmayla duruyordu
/// (<c>MenuItem.InputGesture</c> ve elle yazılmış tünelleyen işleyiciler). Bir komutun
/// kısayolunu öğrenmek için altı dosya okumak gerekiyordu ve çakışmayı görecek kimse yoktu.
/// </remarks>
public interface ICommandRegistry
{
    /// <summary>Tanımlı bütün komutlar, tanım sırasında.</summary>
    IReadOnlyList<CommandDefinition> Definitions { get; }

    /// <summary>Kısayol ataması ya da tanım kümesi değiştiğinde tetiklenir.</summary>
    event EventHandler? Changed;

    CommandDefinition? Find(string commandId);

    /// <summary>
    /// Komutun <b>yürürlükteki</b> kısayolu: kullanıcı atadıysa onunki, yoksa varsayılan.
    /// </summary>
    KeyGesture? GetGesture(string commandId);

    /// <summary>Kullanıcı bu komutun kısayolunu değiştirmiş mi?</summary>
    bool IsCustomized(string commandId);

    /// <summary>
    /// Kısayolu değiştirir. <see langword="null"/> vermek kısayolu <b>kaldırır</b> —
    /// varsayılana dönmek için <see cref="Reset"/> kullanılır.
    /// </summary>
    void SetGesture(string commandId, KeyGesture? gesture);

    /// <summary>Komutu varsayılan kısayoluna döndürür.</summary>
    void Reset(string commandId);

    /// <summary>Bütün kısayolları varsayılana döndürür.</summary>
    void ResetAll();

    /// <summary>
    /// Aynı jesti paylaşan ve bağlamları örtüşen komutlar.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Bunu bizim hesaplamamız şart.</b> P08-T00/M10: Avalonia aynı jeste iki kayıt
    /// bulursa <b>yalnızca ilkini</b> çalıştırır ve hiçbir şey söylemez. Kullanıcı kısayolunu
    /// yeniden atar, çalışmaz ve sebebini göremez.
    /// </remarks>
    IReadOnlyList<ShortcutConflict> Conflicts { get; }

    /// <summary>
    /// Verilen bağlamda bu jeste bağlı komutun kimliği.
    /// </summary>
    /// <remarks>
    /// Bağlam <b>tam olarak</b> istenen kümeyle eşleşir; <see cref="CommandContext.Global"/>
    /// otomatik eklenmez. Aksi hâlde küresel bir kısayol hem pencere bağlamasından hem panel
    /// dağıtımından <b>iki kez</b> çalışırdı.
    /// </remarks>
    string? Resolve(KeyGesture gesture, CommandContext context);

    /// <summary>Verilen bağlamda geçerli komutlar.</summary>
    IReadOnlyList<CommandDefinition> InContext(CommandContext context);
}

/// <inheritdoc cref="ICommandRegistry"/>
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly IReadOnlyList<CommandDefinition> _definitions;
    private readonly Dictionary<string, CommandDefinition> _byId;
    private readonly ISettingsStore _settings;

    public CommandRegistry(ISettingsStore settings)
        : this(settings, DefaultCommandScheme.Definitions)
    {
    }

    public CommandRegistry(ISettingsStore settings, IReadOnlyList<CommandDefinition> definitions)
    {
        _settings = settings;
        _definitions = definitions;

        // Kimlik çakışması bir program hatası, çalışma zamanı durumu değil: iki komut aynı
        // kimliği paylaşırsa kullanıcının yeniden ataması hangisine gideceği belirsizleşir.
        _byId = definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<CommandDefinition> Definitions => _definitions;

    public event EventHandler? Changed;

    public CommandDefinition? Find(string commandId) =>
        _byId.GetValueOrDefault(commandId);

    public KeyGesture? GetGesture(string commandId)
    {
        if (!_byId.TryGetValue(commandId, out CommandDefinition? definition))
        {
            return null;
        }

        if (!_settings.Current.Shortcuts.TryGetValue(commandId, out string? stored))
        {
            return definition.DefaultGesture;
        }

        if (stored.Length == 0)
        {
            // Kullanıcı kısayolu bilerek kaldırmış. Varsayılana dönmek onun kararını
            // geri almak olurdu; bu yüzden "yok" ile "atanmamış" ayrı tutuluyor.
            return null;
        }

        return TryParse(stored) ?? definition.DefaultGesture;
    }

    public bool IsCustomized(string commandId) =>
        _settings.Current.Shortcuts.ContainsKey(commandId);

    public void SetGesture(string commandId, KeyGesture? gesture)
    {
        if (!_byId.ContainsKey(commandId))
        {
            return;
        }

        _settings.Update(s => s.Shortcuts[commandId] = gesture?.ToString() ?? "");

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset(string commandId)
    {
        if (!_settings.Current.Shortcuts.ContainsKey(commandId))
        {
            return;
        }

        _settings.Update(s => s.Shortcuts.Remove(commandId));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ResetAll()
    {
        if (_settings.Current.Shortcuts.Count == 0)
        {
            return;
        }

        _settings.Update(s => s.Shortcuts.Clear());

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<ShortcutConflict> Conflicts
    {
        get
        {
            List<ShortcutConflict> conflicts = [];

            // Jeste göre grupla, sonra grup içinde bağlamı örtüşen çiftleri ara.
            // Aynı jestin farklı panellerde farklı iş yapması ÇAKIŞMA DEĞİLDİR.
            IEnumerable<IGrouping<KeyGesture, CommandDefinition>> groups = _definitions
                .Select(d => (Definition: d, Gesture: GetGesture(d.Id)))
                .Where(pair => pair.Gesture is not null)
                .GroupBy(pair => pair.Gesture!, pair => pair.Definition);

            foreach (IGrouping<KeyGesture, CommandDefinition> group in groups)
            {
                List<CommandDefinition> members = [.. group];

                if (members.Count < 2)
                {
                    continue;
                }

                List<string> clashing = [];

                for (int i = 0; i < members.Count; i++)
                {
                    bool overlapsAny = members
                        .Where((_, j) => j != i)
                        .Any(other => CommandDefinition.ContextsOverlap(members[i].Context, other.Context));

                    if (overlapsAny)
                    {
                        clashing.Add(members[i].Id);
                    }
                }

                if (clashing.Count > 1)
                {
                    conflicts.Add(new ShortcutConflict(group.Key, clashing));
                }
            }

            return conflicts;
        }
    }

    public string? Resolve(KeyGesture gesture, CommandContext context)
    {
        foreach (CommandDefinition definition in _definitions)
        {
            if ((definition.Context & context) == CommandContext.None)
            {
                continue;
            }

            if (gesture.Equals(GetGesture(definition.Id)))
            {
                return definition.Id;
            }
        }

        return null;
    }

    public IReadOnlyList<CommandDefinition> InContext(CommandContext context) =>
        [.. _definitions.Where(d => (d.Context & context) != CommandContext.None)];

    /// <summary>
    /// Ayar dosyasındaki jest metnini çözer.
    /// </summary>
    /// <remarks>
    /// Hoşgörülü: elle düzenlenmiş bozuk bir satır yalnızca o komutu varsayılanına düşürür.
    /// <c>KeyGesture.Parse</c> tanımadığı metinde istisna atıyor (P08-T00/M06) ve bu istisna
    /// yakalanmasaydı bozuk tek bir satır <b>uygulamayı açılmaz</b> hâle getirirdi.
    /// </remarks>
    private static KeyGesture? TryParse(string text)
    {
        try
        {
            return KeyGesture.Parse(text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }
}
