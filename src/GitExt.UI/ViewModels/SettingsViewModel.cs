using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core.Git;
using GitExt.UI.Commands;
using GitExt.UI.Settings;
using GitExt.UI.Themes;

namespace GitExt.UI.ViewModels;

/// <summary>
/// Ayarlar ekranı (P08-T15).
/// </summary>
/// <remarks>
/// Üç bölüm: görünüm, kısayollar ve git kimliği. Git ayarları buraya <b>ait</b>, çünkü
/// kullanıcı açısından "commit'lerimde hangi isim görünüyor" bir uygulama ayarı gibi
/// hissediliyor — nerede saklandığı (git'in kendi dosyası) uygulamanın iç meselesi.
/// </remarks>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppearanceService _appearance;
    private readonly ISettingsStore _settings;
    private readonly IGitConfigWriter? _config;
    private readonly string? _workingDirectory;

    private bool _loading;

    [ObservableProperty]
    private ThemePreference _theme;

    [ObservableProperty]
    private PalettePreference _palette;

    [ObservableProperty]
    private double _uiFontSize;

    [ObservableProperty]
    private double _monospaceFontSize;

    [ObservableProperty]
    private string _monospaceFontFamily = string.Empty;

    [ObservableProperty]
    private string _globalUserName = string.Empty;

    [ObservableProperty]
    private string _globalUserEmail = string.Empty;

    [ObservableProperty]
    private string _localUserName = string.Empty;

    [ObservableProperty]
    private string _localUserEmail = string.Empty;

    [ObservableProperty]
    private string _globalEditor = string.Empty;

    [ObservableProperty]
    private string _gitError = string.Empty;

    public SettingsViewModel(
        IAppearanceService appearance,
        ISettingsStore settings,
        ICommandRegistry registry,
        IGitConfigWriter? config = null,
        string? workingDirectory = null)
    {
        _appearance = appearance;
        _settings = settings;
        _config = config;
        _workingDirectory = workingDirectory;

        Shortcuts = new ShortcutSettingsViewModel(registry);

        SaveGitCommand = new AsyncRelayCommand(SaveGitAsync, () => CanEditGit);

        LoadFromSettings();
    }

    /// <summary>Kısayol düzenleme bölümü (P08-T03).</summary>
    public ShortcutSettingsViewModel Shortcuts { get; }

    /// <summary>
    /// Yerel git ayarları düzenlenebilir mi?
    /// </summary>
    /// <remarks>
    /// 🔴 Depo açık değilken <c>--local</c> git tarafından <b>reddediliyor</b>
    /// (<c>fatal: --local can only be used inside a git repository</c>, çıkış kodu 128).
    /// Alanları etkin bırakmak, kullanıcıya kaydedilmeyecek bir kutu sunmak olurdu.
    /// </remarks>
    public bool CanEditLocal => _workingDirectory is { Length: > 0 };

    public bool CanEditGit => _config is not null;

    public IReadOnlyList<ThemePreference> Themes { get; } =
        [ThemePreference.Light, ThemePreference.Dark, ThemePreference.System];

    public IReadOnlyList<PalettePreference> Palettes { get; } =
        [PalettePreference.Default, PalettePreference.ColorBlindSafe];

    public IAsyncRelayCommand SaveGitCommand { get; }

    /// <summary>Git ayarlarını diskten okur.</summary>
    public async Task LoadGitAsync(CancellationToken cancellationToken = default)
    {
        if (_config is null)
        {
            return;
        }

        string probe = _workingDirectory ?? Directory.GetCurrentDirectory();

        try
        {
            GlobalUserName = await Read(probe, "user.name", GitConfigScope.Global).ConfigureAwait(true);
            GlobalUserEmail = await Read(probe, "user.email", GitConfigScope.Global).ConfigureAwait(true);
            GlobalEditor = await Read(probe, "core.editor", GitConfigScope.Global).ConfigureAwait(true);

            if (CanEditLocal)
            {
                LocalUserName = await Read(_workingDirectory!, "user.name", GitConfigScope.Local).ConfigureAwait(true);
                LocalUserEmail = await Read(_workingDirectory!, "user.email", GitConfigScope.Local).ConfigureAwait(true);
            }

            GitError = string.Empty;
        }
        catch (GitException ex)
        {
            GitError = ex.Message;
        }

        async Task<string> Read(string directory, string key, GitConfigScope scope) =>
            await _config.GetScopedAsync(directory, key, scope, cancellationToken).ConfigureAwait(true) ?? "";
    }

    partial void OnThemeChanged(ThemePreference value) => Apply(() => _appearance.SetTheme(value));

    partial void OnPaletteChanged(PalettePreference value) => Apply(() => _appearance.SetPalette(value));

    partial void OnUiFontSizeChanged(double value) => ApplyFontSizes();

    partial void OnMonospaceFontSizeChanged(double value) => ApplyFontSizes();

    partial void OnMonospaceFontFamilyChanged(string value) =>
        Apply(() => _appearance.SetMonospaceFont(value));

    private void ApplyFontSizes() =>
        Apply(() => _appearance.SetFontSizes(UiFontSize, MonospaceFontSize));

    /// <summary>
    /// Değişikliği uygular — ama ekran <b>yüklenirken</b> değil.
    /// </summary>
    /// <remarks>
    /// Yükleme sırasında özellik atamaları da <c>PropertyChanged</c> tetikliyor. Süzülmezse
    /// ekranı açmak, hiçbir şey değiştirilmemişken ayarları yeniden yazardı — ve ayar
    /// dosyasının değişme zamanı kullanıcının hiç dokunmadığı bir anda kayardı.
    /// </remarks>
    private void Apply(Action action)
    {
        if (!_loading)
        {
            action();
        }
    }

    private void LoadFromSettings()
    {
        _loading = true;

        try
        {
            AppearanceSettings appearance = _settings.Current.Appearance;

            Theme = SettingsEnum.Parse(appearance.Theme, ThemePreference.Light);
            Palette = SettingsEnum.Parse(appearance.Palette, PalettePreference.Default);
            UiFontSize = appearance.UiFontSize;
            MonospaceFontSize = appearance.MonospaceFontSize;
            MonospaceFontFamily = appearance.MonospaceFontFamily;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SaveGitAsync()
    {
        if (_config is null)
        {
            return;
        }

        string probe = _workingDirectory ?? Directory.GetCurrentDirectory();

        try
        {
            await _config.SetAsync(probe, "user.name", GlobalUserName, GitConfigScope.Global).ConfigureAwait(true);
            await _config.SetAsync(probe, "user.email", GlobalUserEmail, GitConfigScope.Global).ConfigureAwait(true);
            await _config.SetAsync(probe, "core.editor", GlobalEditor, GitConfigScope.Global).ConfigureAwait(true);

            if (CanEditLocal)
            {
                await _config.SetAsync(_workingDirectory!, "user.name", LocalUserName, GitConfigScope.Local)
                    .ConfigureAwait(true);
                await _config.SetAsync(_workingDirectory!, "user.email", LocalUserEmail, GitConfigScope.Local)
                    .ConfigureAwait(true);
            }

            GitError = string.Empty;
        }
        catch (GitException ex)
        {
            // Ham stderr birincil mesaj değil ama erişilebilir kalmalı (P02-T12).
            GitError = ex.Message;
        }
    }
}
