using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;

namespace GitExt.UI.Commands;

/// <summary>
/// Küresel kısayolları pencereye kurar ve menü etiketlerini kayıtla eşler (P08-T01).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Bu sınıfın varlık sebebi gerçek bir kusur.</b> P08-T00/M03'te ölçüldü:
/// <c>MenuItem.InputGesture</c> komutu <b>çalıştırmıyor</b>, yalnızca etiketi çiziyor.
/// <c>MainWindow.axaml</c>'de <c>InputGesture="F5"</c> yazıyordu ve başka hiçbir bağlama
/// yoktu — yani <b>F5 ölü bir tuştu</b>: menüde kısayolu görünüyor, basınca hiçbir şey
/// olmuyordu.
/// </para>
/// <para>
/// Bu yüzden burada iki iş birlikte yapılıyor: jest hem <c>KeyBindings</c>'e (işi yapan yer)
/// hem <c>InputGesture</c>'a (etiket) <b>aynı kaynaktan</b> yazılıyor. Ayrılamazlar; ayrılırsa
/// menüde yazan kısayol ile çalışan kısayol sessizce farklılaşır.
/// </para>
/// </remarks>
public sealed class GlobalShortcuts : IDisposable
{
    private readonly Window _window;
    private readonly ICommandRegistry _registry;
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MenuItem> _menuItems = new(StringComparer.Ordinal);

    public GlobalShortcuts(Window window, ICommandRegistry registry)
    {
        _window = window;
        _registry = registry;

        _registry.Changed += OnRegistryChanged;
    }

    /// <summary>
    /// Komut kimliğini çalıştıracak yere ileten yönlendirici.
    /// </summary>
    /// <remarks>
    /// Komut paleti buradan besleniyor: kısayolla palet <b>aynı</b> yürütme yolunu
    /// kullanmasaydı bir komut birinde çalışıp diğerinde çalışmayabilirdi.
    /// </remarks>
    public CommandRouter Router { get; } = new();

    /// <summary>Komutu bir kimliğe bağlar.</summary>
    public GlobalShortcuts Bind(string commandId, ICommand command)
    {
        if (_registry.Find(commandId) is null)
        {
            throw new ArgumentException($"Unknown command id: {commandId}", nameof(commandId));
        }

        _commands[commandId] = command;
        Router.Register(commandId, command);

        return this;
    }

    /// <summary>
    /// Menü öğesini bir kimliğe bağlar: hem komutu hem <b>gösterilen</b> jesti kayıttan alır.
    /// </summary>
    public GlobalShortcuts BindMenu(string commandId, MenuItem item)
    {
        _menuItems[commandId] = item;

        if (_commands.TryGetValue(commandId, out ICommand? command) && item.Command is null)
        {
            item.Command = command;
        }

        return this;
    }

    /// <summary>Bağlamaları pencereye uygular. Kayıt değişince kendiliğinden yinelenir.</summary>
    public void Apply()
    {
        _window.KeyBindings.Clear();

        HashSet<KeyGesture> taken = [];

        foreach (CommandDefinition definition in _registry.InContext(CommandContext.Global))
        {
            KeyGesture? gesture = _registry.GetGesture(definition.Id);

            if (_menuItems.TryGetValue(definition.Id, out MenuItem? item))
            {
                // Etiket her zaman güncellenir — komut bağlı olmasa bile menüde yazan
                // kısayol ile gerçekte çalışan kısayol ayrışmasın.
                item.InputGesture = gesture;
            }

            if (gesture is null || !_commands.TryGetValue(definition.Id, out ICommand? command))
            {
                continue;
            }

            // 🔴 Aynı jesti iki kez kaydetmek sessizce ikinciyi öldürür (P08-T00/M10).
            // Çakışma zaten kayıtta raporlanıyor; burada ikinciyi HİÇ kaydetmemek,
            // "bazen çalışıyor" davranışından dürüst.
            if (!taken.Add(gesture))
            {
                continue;
            }

            _window.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
        }
    }

    public void Dispose() => _registry.Changed -= OnRegistryChanged;

    private void OnRegistryChanged(object? sender, EventArgs e) => Apply();
}
