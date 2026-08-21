using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core.Git;
using GitExt.UI.Commands;
using GitExt.UI.Localization;
using GitExt.UI.Settings;
using GitExt.UI.Themes;

namespace GitExt.UI.ViewModels;

/// <summary>
/// The settings screen (P08-T15).
/// </summary>
/// <remarks>
/// Three sections: appearance, shortcuts and git identity. The git settings <b>belong</b> here,
/// because from the user's point of view "which name shows on my commits" feels like an application
/// setting — where it is stored (git's own file) is the application's internal business.
/// </remarks>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppearanceService _appearance;
    private readonly ISettingsStore _settings;
    private readonly ITranslator? _translator;
    private readonly IGitConfigWriter? _config;
    private readonly string? _workingDirectory;

    private bool _loading;

    [ObservableProperty]
    private LanguageInfo? _language;

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

    /// <summary>
    /// Should the repository open at shutdown be reopened at startup (P12-T04)?
    /// </summary>
    /// <remarks>
    /// GitExtensions has the same switch (<c>StartWithRecentWorkingDir</c>) and it is off by
    /// default there too: the application starts on the dashboard and the repository is picked
    /// from the list.
    /// </remarks>
    [ObservableProperty]
    private bool _startWithRecentRepository;

    /// <summary>Should the application check for newer versions (P13-T01)?</summary>
    [ObservableProperty]
    private bool _checkForUpdates;

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
        string? workingDirectory = null,
        ITranslator? translator = null)
    {
        _appearance = appearance;
        _settings = settings;
        _translator = translator;
        _config = config;
        _workingDirectory = workingDirectory;

        // When no translator is supplied (as in some tests) the language list stays empty and the
        // dropdown appears disabled — it does not crash.
        Languages = translator?.Available ?? [];

        Shortcuts = new ShortcutSettingsViewModel(registry);

        SaveGitCommand = new AsyncRelayCommand(SaveGitAsync, () => CanEditGit);

        LoadFromSettings();
    }

    /// <summary>The shortcut editing section (P08-T03).</summary>
    public ShortcutSettingsViewModel Shortcuts { get; }

    /// <summary>
    /// Can the local git settings be edited?
    /// </summary>
    /// <remarks>
    /// 🔴 With no repository open, <c>--local</c> is <b>refused</b> by git
    /// (<c>fatal: --local can only be used inside a git repository</c>, exit code 128).
    /// Leaving the fields enabled would offer the user a box that will not be saved.
    /// </remarks>
    public bool CanEditLocal => _workingDirectory is { Length: > 0 };

    public bool CanEditGit => _config is not null;

    /// <summary>
    /// The available languages (P11-T07).
    /// </summary>
    /// <remarks>
    /// Unlike the Theme/Palette lists this one is not fixed: it comes from the embedded language files
    /// at runtime. Adding a new JSON to the <c>Locales/</c> folder is enough for another row to appear
    /// in this list.
    /// </remarks>
    public IReadOnlyList<LanguageInfo> Languages { get; }

    public IReadOnlyList<ThemePreference> Themes { get; } =
        [ThemePreference.Light, ThemePreference.Dark, ThemePreference.System];

    public IReadOnlyList<PalettePreference> Palettes { get; } =
        [PalettePreference.Default, PalettePreference.ColorBlindSafe];

    public IAsyncRelayCommand SaveGitCommand { get; }

    /// <summary>Reads the git settings from disk.</summary>
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
            GitError = Loc.GitError(ex);
        }

        async Task<string> Read(string directory, string key, GitConfigScope scope) =>
            await _config.GetScopedAsync(directory, key, scope, cancellationToken).ConfigureAwait(true) ?? "";
    }

    partial void OnLanguageChanged(LanguageInfo? value)
    {
        if (value is not null)
        {
            Apply(() => _translator?.Use(value.Code));
        }
    }

    partial void OnThemeChanged(ThemePreference value) => Apply(() => _appearance.SetTheme(value));

    partial void OnPaletteChanged(PalettePreference value) => Apply(() => _appearance.SetPalette(value));

    partial void OnUiFontSizeChanged(double value) => ApplyFontSizes();

    partial void OnMonospaceFontSizeChanged(double value) => ApplyFontSizes();

    partial void OnCheckForUpdatesChanged(bool value) =>
        Apply(() => _settings.Update(settings => settings.General.CheckForUpdates = value));

    partial void OnStartWithRecentRepositoryChanged(bool value) =>
        Apply(() => _settings.Update(settings => settings.General.StartWithRecentWorkingDir = value));

    partial void OnMonospaceFontFamilyChanged(string value) =>
        Apply(() => _appearance.SetMonospaceFont(value));

    private void ApplyFontSizes() =>
        Apply(() => _appearance.SetFontSizes(UiFontSize, MonospaceFontSize));

    /// <summary>
    /// Applies the change — but not while the screen is <b>loading</b>.
    /// </summary>
    /// <remarks>
    /// During loading, the property assignments raise <c>PropertyChanged</c> too. Unless they are
    /// filtered out, opening the screen would rewrite the settings with nothing having been changed —
    /// and the settings file's modification time would shift at a moment the user never touched it.
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

            // The active language is read from the translator itself, NOT from the code in the
            // settings: the setting may be empty (first run) or hold an unrecognised code; in both
            // cases the translator has already fallen back to English and the dropdown should show that.
            Language = _translator is null
                ? null
                : Languages.FirstOrDefault(l => l.Code == _translator.Current);

            Theme = SettingsEnum.Parse(appearance.Theme, ThemePreference.Light);
            Palette = SettingsEnum.Parse(appearance.Palette, PalettePreference.Default);
            UiFontSize = appearance.UiFontSize;
            MonospaceFontSize = appearance.MonospaceFontSize;
            MonospaceFontFamily = appearance.MonospaceFontFamily;
            StartWithRecentRepository = _settings.Current.General.StartWithRecentWorkingDir;
            CheckForUpdates = _settings.Current.General.CheckForUpdates;
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
            // Raw stderr is not the primary message but must stay reachable (P02-T12).
            GitError = Loc.GitError(ex);
        }
    }
}
