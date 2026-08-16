using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using GitExt.UI.Settings;

namespace GitExt.UI.Localization;

/// <summary>
/// Supplies the UI texts from the language files and switches language at runtime (P11-T01).
/// </summary>
public interface ITranslator : INotifyPropertyChanged
{
    /// <summary>The text for the key. Never crashes when the key is missing.</summary>
    string this[string key] { get; }

    /// <summary>Yer tutuculu metni doldurur: <c>"{0} commits loaded"</c>.</summary>
    string Format(string key, params object?[] arguments);

    /// <summary>The languages found in the embedded language files; sorted by name.</summary>
    IReadOnlyList<LanguageInfo> Available { get; }

    /// <summary>Etkin dilin kodu.</summary>
    string Current { get; }

    /// <summary>Switches the language and persists the preference.</summary>
    void Use(string code);

    /// <summary>Applies the stored preference; failing that the system language, and failing that English.</summary>
    void ApplyStored();
}

/// <inheritdoc cref="ITranslator"/>
/// <remarks>
/// <para>
/// The languages are <b>discovered from the embedded resources</b>, not from a list written into the
/// code. Thanks to the <c>Locales/*.json</c> wildcard entry, dropping <c>fr.json</c> into the folder
/// is enough for that language to appear in the list — no C# file is touched (measured, P11-T00).
/// </para>
/// <para>
/// <b>Switching language does not reopen the windows.</b> The class implements
/// <see cref="INotifyPropertyChanged"/> and raises <c>PropertyChanged(null)</c> on a language change;
/// Avalonia reads that as "everything on this object changed" and refreshes every binding set up
/// through the indexer.
/// </para>
/// </remarks>
public sealed class Translator : ITranslator
{
    /// <summary>
    /// The common prefix of the embedded resource names: <c>GitExt.UI.Locales.</c>
    /// </summary>
    private const string ResourcePrefix = "GitExt.UI.Locales.";

    /// <summary>The source language. A key missing in another language falls back to this one.</summary>
    internal const string FallbackLanguage = "en";

    private readonly ISettingsStore _settings;

    /// <summary>Dil kodu → (anahtar → metin).</summary>
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogs;

    private IReadOnlyDictionary<string, string> _active;
    private IReadOnlyDictionary<string, string> _fallback;

    public Translator(ISettingsStore settings)
        : this(settings, ReadEmbeddedResources(typeof(Translator).Assembly))
    {
    }

    private Translator(ISettingsStore settings, IReadOnlyDictionary<string, Func<Stream>> resources)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _catalogs = LoadCatalogs(resources, out List<LanguageInfo> languages);

        // 🔴 The fallback language comes from the CODE, NOT from a file (P11-T10). It used to use
        // `_catalogs["en"]`, and if `en.json` were deleted or corrupted the fallback became an EMPTY
        // dictionary: every key not present in Turkish would show in the UI as the raw key
        // ("settings.language"). Measured and confirmed.
        //
        // BuiltInEnglish is GENERATED from the same file (tools/i18n/generate-fallback.py) and CI
        // verifies the two have not diverged — a second, hand-written English source would sooner or
        // later drift apart.
        _fallback = BuiltInEnglish.Entries;
        _active = _fallback;
        Current = FallbackLanguage;

        // English must be selectable even without `en.json`: the embedded copy is already complete.
        if (!languages.Any(l => l.Code == FallbackLanguage))
        {
            languages.Add(new LanguageInfo(FallbackLanguage, "English"));
            languages.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        Available = languages;
    }

    /// <summary>
    /// The test constructor that supplies the language files from outside.
    /// </summary>
    /// <remarks>
    /// The discovery logic (resource name → language code → catalog) runs exactly as before; the only
    /// thing that changes is where the resources are read from. Without this hook, the requirement
    /// "a new language dropped into the folder appears in the list" could only be tested by
    /// generating an assembly at runtime.
    /// </remarks>
    internal static Translator ForTesting(
        ISettingsStore settings,
        IReadOnlyDictionary<string, Func<Stream>> resources) => new(settings, resources);

    /// <summary>Supplies the <c>Locales/*.json</c> resources embedded in the assembly, with their names.</summary>
    private static Dictionary<string, Func<Stream>> ReadEmbeddedResources(Assembly assembly)
    {
        Dictionary<string, Func<Stream>> resources = new(StringComparer.Ordinal);

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".json", StringComparison.Ordinal))
            {
                resources[name] = () => assembly.GetManifestResourceStream(name)!;
            }
        }

        return resources;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LanguageInfo> Available { get; }

    public string Current { get; private set; }

    /// <summary>
    /// The text for the key; in order, the active language → English → <b>the key itself</b>.
    /// </summary>
    /// <remarks>
    /// Returning an empty string for a missing key would be the worst option: an empty label appears
    /// in the UI and there is no telling where the gap came from. Showing the key itself
    /// (<c>settings.title</c>) makes the omission visible <b>by eye</b> and says directly which key
    /// needs adding.
    /// </remarks>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (_active.TryGetValue(key, out string? text) || _fallback.TryGetValue(key, out text))
            {
                return text;
            }

            return key;
        }
    }

    public string Format(string key, params object?[] arguments)
    {
        string template = this[key];

        if (arguments is not { Length: > 0 })
        {
            return template;
        }

        try
        {
            // InvariantCulture is deliberate: InvariantGlobalization=true is on, and asking for any
            // other culture throws CultureNotFoundException (measured, P11-T00).
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            // A broken placeholder in a translation (something like "{0") must not crash the app.
            // The raw template is shown: the error stays visible but the UI stays up.
            return template;
        }
    }

    public void Use(string code)
    {
        if (!TrySelect(code))
        {
            return;
        }

        _settings.Update(s => s.General.Language = Current);
    }

    public void ApplyStored()
    {
        string stored = _settings.Current.General.Language;

        if (!string.IsNullOrWhiteSpace(stored) && TrySelect(stored))
        {
            return;
        }

        // With no preference the system language is tried. Under InvariantGlobalization
        // CurrentUICulture comes back empty; in that case we silently stay on English.
        string system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        if (!string.IsNullOrWhiteSpace(system) && TrySelect(system))
        {
            return;
        }

        TrySelect(FallbackLanguage);
    }

    private bool TrySelect(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string normalized = code.Trim().ToLowerInvariant();

        if (!_catalogs.TryGetValue(normalized, out IReadOnlyDictionary<string, string>? catalog))
        {
            // English is always selectable: even without its file the embedded copy is complete
            // (P11-T10). For any other language, no file really does mean not selectable.
            if (!string.Equals(normalized, FallbackLanguage, StringComparison.Ordinal))
            {
                return false;
            }

            catalog = BuiltInEnglish.Entries;
        }

        if (string.Equals(normalized, Current, StringComparison.Ordinal) && ReferenceEquals(_active, catalog))
        {
            return false;
        }

        _active = catalog;
        Current = normalized;

        // null: "EVERYTHING on this object changed". Every indexer binding is refreshed.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        return true;
    }

    /// <summary>
    /// Builds the language catalogs from the resource streams.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Bozuk bir dil dosyası uygulamayı açılmaz hâle getirmemeli; o dil atlanıyor.")]
    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadCatalogs(
        IReadOnlyDictionary<string, Func<Stream>> resources,
        out List<LanguageInfo> languages)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> catalogs = new(StringComparer.Ordinal);
        List<LanguageInfo> found = [];

        foreach ((string resource, Func<Stream> open) in resources)
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            string code = resource[ResourcePrefix.Length..^".json".Length].ToLowerInvariant();

            try
            {
                using Stream? stream = open();

                if (stream is null)
                {
                    continue;
                }

                LocaleFile? file = JsonSerializer.Deserialize(stream, LocaleJsonContext.Default.LocaleFile);

                if (file is null)
                {
                    continue;
                }

                Dictionary<string, string> entries = new(StringComparer.Ordinal);

                foreach ((string key, JsonElement value) in file.Entries ?? [])
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        entries[key] = value.GetString() ?? string.Empty;
                    }
                }

                catalogs[code] = entries;

                // With no name, the code itself is shown — the language does not vanish from the list.
                string name = file.Meta?.Name is { Length: > 0 } declared ? declared : code;
                found.Add(new LanguageInfo(code, name));
            }
            catch (Exception)
            {
                // Broken JSON: this language is skipped, the others load.
            }
        }

        languages = [.. found.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)];
        return catalogs;
    }
}
