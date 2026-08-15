using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using GitExt.UI.Settings;

namespace GitExt.UI.Localization;

/// <summary>
/// Arayüz metinlerini dil dosyalarından sağlar ve çalışma anında dil değiştirir (P11-T01).
/// </summary>
public interface ITranslator : INotifyPropertyChanged
{
    /// <summary>Anahtara karşılık gelen metin. Anahtar yoksa asla çökmez.</summary>
    string this[string key] { get; }

    /// <summary>Yer tutuculu metni doldurur: <c>"{0} commits loaded"</c>.</summary>
    string Format(string key, params object?[] arguments);

    /// <summary>Gömülü dil dosyalarından bulunan diller; ada göre sıralı.</summary>
    IReadOnlyList<LanguageInfo> Available { get; }

    /// <summary>Etkin dilin kodu.</summary>
    string Current { get; }

    /// <summary>Dili değiştirir ve tercihi kalıcılaştırır.</summary>
    void Use(string code);

    /// <summary>Kayıtlı tercihi uygular; yoksa sistem dilini, o da tanınmıyorsa İngilizceyi.</summary>
    void ApplyStored();
}

/// <inheritdoc cref="ITranslator"/>
/// <remarks>
/// <para>
/// Diller <b>gömülü kaynaklardan keşfediliyor</b>, koda yazılmış bir listeden değil.
/// <c>Locales/*.json</c> joker girdisi sayesinde klasöre <c>fr.json</c> eklemek, o dilin
/// listede belirmesi için yeterli — hiçbir C# dosyasına dokunulmuyor (ölçüldü, P11-T00).
/// </para>
/// <para>
/// <b>Dil geçişi pencereleri yeniden açmıyor.</b> Sınıf <see cref="INotifyPropertyChanged"/>
/// uyguluyor ve dil değişiminde <c>PropertyChanged(null)</c> yayınlıyor; Avalonia bunu
/// "bu nesnedeki her şey değişti" olarak okuyup indeksleyici üzerinden kurulmuş tüm
/// bağlamaları tazeliyor.
/// </para>
/// </remarks>
public sealed class Translator : ITranslator
{
    /// <summary>
    /// Gömülü kaynak adlarının ortak öneki: <c>GitExt.UI.Locales.</c>
    /// </summary>
    private const string ResourcePrefix = "GitExt.UI.Locales.";

    /// <summary>Kaynak dil. Bir anahtar başka dilde eksikse buraya düşülüyor.</summary>
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
        Available = languages;

        _fallback = _catalogs.GetValueOrDefault(FallbackLanguage) ?? new Dictionary<string, string>();
        _active = _fallback;
        Current = FallbackLanguage;
    }

    /// <summary>
    /// Dil dosyalarını dışarıdan veren test kurucusu.
    /// </summary>
    /// <remarks>
    /// Keşif mantığı (kaynak adı → dil kodu → katalog) aynen çalışıyor; değişen yalnızca
    /// kaynakların nereden okunduğu. Bu kanca olmadan "klasöre yeni dil eklenince listede
    /// beliriyor" gereksinimi ancak çalışma anında derleme üreterek test edilebilirdi.
    /// </remarks>
    internal static Translator ForTesting(
        ISettingsStore settings,
        IReadOnlyDictionary<string, Func<Stream>> resources) => new(settings, resources);

    /// <summary>Derlemeye gömülü <c>Locales/*.json</c> kaynaklarını adlarıyla verir.</summary>
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
    /// Anahtarın karşılığı; sırasıyla etkin dil → İngilizce → <b>anahtarın kendisi</b>.
    /// </summary>
    /// <remarks>
    /// Eksik anahtarda boş string döndürmek en kötü seçenek olurdu: arayüzde boş bir etiket
    /// belirir ve eksikliğin nereden geldiği anlaşılmaz. Anahtarın kendisi gösterilince
    /// (<c>settings.title</c>) hem eksik olduğu <b>gözle</b> görülüyor hem de hangi anahtarın
    /// eklenmesi gerektiği doğrudan okunuyor.
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
            // InvariantCulture bilinçli: InvariantGlobalization=true açık, başka bir kültür
            // istemek CultureNotFoundException fırlatıyor (ölçüldü, P11-T00).
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            // Çeviride bozuk bir yer tutucu ("{0" gibi) uygulamayı çökertmemeli.
            // Ham şablon gösteriliyor: hata görünür kalıyor ama arayüz ayakta.
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

        // Tercih yoksa sistem dili deneniyor. InvariantGlobalization altında
        // CurrentUICulture boş geliyor; o durumda sessizce İngilizce kalıyoruz.
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
            return false;
        }

        if (string.Equals(normalized, Current, StringComparison.Ordinal) && ReferenceEquals(_active, catalog))
        {
            return false;
        }

        _active = catalog;
        Current = normalized;

        // null: "bu nesnedeki HER ŞEY değişti". İndeksleyici bağlamalarının tamamı tazeleniyor.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        return true;
    }

    /// <summary>
    /// Dil kataloglarını kaynak akışlarından kurar.
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

                // Ad yoksa kodun kendisi gösteriliyor — dil listede kaybolmuyor.
                string name = file.Meta?.Name is { Length: > 0 } declared ? declared : code;
                found.Add(new LanguageInfo(code, name));
            }
            catch (Exception)
            {
                // Bozuk JSON: bu dil atlanıyor, diğerleri yükleniyor.
            }
        }

        languages = [.. found.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)];
        return catalogs;
    }
}
