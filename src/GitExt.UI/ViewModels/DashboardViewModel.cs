using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using GitExt.Core;
using GitExt.UI.Localization;
using GitExt.UI.Storage;

namespace GitExt.UI.ViewModels;

/// <summary>
/// One repository tile on the dashboard (P12-T03).
/// </summary>
/// <remarks>
/// Its counterpart in GitExtensions is a <c>ListViewItem</c> in <c>UserRepositoriesList</c>: the
/// caption on the first line, the current branch on the second in a smaller font, a star when the
/// repository is filed under a category, and a different folder icon when the path can no longer be
/// reached.
/// </remarks>
public sealed class DashboardRepositoryItem
{
    public DashboardRepositoryItem(
        DashboardViewModel owner,
        RecentRepository repository,
        RepositoryHeadInfo head)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(repository);

        Owner = owner;
        Path = repository.Path;
        Category = repository.Category;
        IsValid = head.IsRepository;

        Branch = head.BranchName ?? (head.IsDetached ? Loc.T("dashboard.detached_head") : string.Empty);

        // The folder name makes the list easy to scan; the full path is the tooltip, exactly as
        // GitExtensions puts it in `ToolTipText`.
        Name = System.IO.Path.GetFileName(repository.Path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));

        if (string.IsNullOrEmpty(Name))
        {
            Name = repository.Path;
        }
    }

    /// <summary>The dashboard this tile belongs to — the context menu commands live there.</summary>
    public DashboardViewModel Owner { get; }

    public string Path { get; }

    public string Name { get; }

    /// <summary>The category, or <see langword="null"/> when the repository is merely recent.</summary>
    public string? Category { get; }

    public bool IsFavourite => !string.IsNullOrWhiteSpace(Category);

    /// <summary>The checked-out branch; empty when the repository could not be read.</summary>
    public string Branch { get; }

    public bool HasBranch => Branch.Length > 0;

    /// <summary>
    /// Is the path still a repository?
    /// </summary>
    /// <remarks>
    /// An entry that fails this is <b>not removed</b>, it is drawn with the error icon — an
    /// unmounted disk is not a reason to throw away the user's list. Removing them is a separate,
    /// deliberate context menu action.
    /// </remarks>
    public bool IsValid { get; }

    public ICommand OpenCommand => Owner.OpenCommand;

    public override string ToString() => Path;
}

/// <summary>
/// A group of tiles on the dashboard — "Recent repositories" or a category (P12-T03).
/// </summary>
public sealed class DashboardGroup
{
    public DashboardGroup(string header, bool isRecent, IReadOnlyList<DashboardRepositoryItem> items)
    {
        Header = header;
        IsRecent = isRecent;
        Items = items;
    }

    public string Header { get; }

    /// <summary>Is this the built-in "Recent repositories" group rather than a category?</summary>
    /// <remarks>
    /// Decides which context menu the header offers: a category can be renamed and deleted, the
    /// recent group can only be cleared. Same split as GitExtensions'
    /// <c>ListView1_GroupTaskLinkClick</c>.
    /// </remarks>
    public bool IsRecent { get; }

    public IReadOnlyList<DashboardRepositoryItem> Items { get; }
}

/// <summary>
/// An entry of the tile's "Categories" submenu (P12-T03).
/// </summary>
public sealed class DashboardCategoryChoice
{
    public DashboardCategoryChoice(string header, ICommand command, object? parameter, bool isEnabled)
    {
        Header = header;
        Command = command;
        Parameter = parameter;
        IsEnabled = isEnabled;
    }

    public string Header { get; }

    public ICommand Command { get; }

    public object? Parameter { get; }

    /// <summary>
    /// The category the repository is already in is disabled — as it is in GitExtensions.
    /// </summary>
    public bool IsEnabled { get; }
}

/// <summary>
/// The dashboard: the repository list shown when no repository is open (P12-T03).
/// </summary>
/// <remarks>
/// <para>
/// Its counterpart in GitExtensions is <c>UserRepositoriesList</c>. What is copied is the
/// <b>structure</b>: a search box, "Recent repositories" first, the categories below it in
/// alphabetical order, and a per-tile context menu in the order
/// <i>Show in folder · Categories · Remove project from the list · Remove missing projects</i>.
/// </para>
/// <para>
/// The branch names are read from the file system rather than by starting git — see
/// <see cref="RepositoryHead"/> for the measurement.
/// </para>
/// </remarks>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly IRecentRepositoryStore _store;

    /// <summary>Everything the store holds, before the search filter.</summary>
    private IReadOnlyList<RecentRepository> _repositories = [];

    public DashboardViewModel(
        IRecentRepositoryStore store,
        ICommand openCommand,
        Func<string, RepositoryHeadInfo>? probe = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(openCommand);

        _store = store;
        Probe = probe ?? RepositoryHead.Read;
        OpenCommand = openCommand;

        ShowInFolderCommand = new RelayCommand(ShowInFolder, () => SelectedItem is not null);
        RemoveFromListCommand = new AsyncRelayCommand(RemoveFromListAsync, () => SelectedItem is not null);
        RemoveMissingCommand = new AsyncRelayCommand(RemoveMissingAsync, () => HasInvalidRepositories);
        SetCategoryCommand = new AsyncRelayCommand<string>(SetCategoryAsync);
        AddCategoryCommand = new AsyncRelayCommand(AddCategoryAsync);
        RenameCategoryCommand = new AsyncRelayCommand<DashboardGroup>(RenameCategoryAsync);
        DeleteCategoryCommand = new AsyncRelayCommand<DashboardGroup>(DeleteCategoryAsync);
        ClearRecentCommand = new AsyncRelayCommand(ClearRecentAsync);
    }

    /// <summary>Opens a repository; supplied by the main ViewModel.</summary>
    public ICommand OpenCommand { get; }

    /// <summary>
    /// How a repository's state is read. Replaceable so tests do not depend on the file system.
    /// </summary>
    /// <remarks>
    /// The default reads <c>HEAD</c> from disk (see <see cref="RepositoryHead"/>). A test — or the
    /// screenshot, which has to come out the same on every run — hands in its own answers instead
    /// of creating repositories on disk.
    /// </remarks>
    public Func<string, RepositoryHeadInfo> Probe { get; set; }

    /// <summary>Shows the dialogs. Without it the actions that ask a question do nothing.</summary>
    public IDashboardPrompt? Prompt { get; set; }

    /// <summary>Opens a folder in the system's file manager. Set by the view.</summary>
    public Action<string>? OpenFolderInShell { get; set; }

    /// <summary>The groups on screen, "Recent repositories" first.</summary>
    public ObservableCollection<DashboardGroup> Groups { get; } = [];

    /// <summary>The categories in use, alphabetically.</summary>
    public ObservableCollection<string> Categories { get; } = [];

    /// <summary>The "Categories" submenu of the tile under the pointer.</summary>
    public ObservableCollection<DashboardCategoryChoice> CategoryChoices { get; } = [];

    public IRelayCommand ShowInFolderCommand { get; }

    public IAsyncRelayCommand RemoveFromListCommand { get; }

    public IAsyncRelayCommand RemoveMissingCommand { get; }

    public IAsyncRelayCommand<string> SetCategoryCommand { get; }

    public IAsyncRelayCommand AddCategoryCommand { get; }

    public IAsyncRelayCommand<DashboardGroup> RenameCategoryCommand { get; }

    public IAsyncRelayCommand<DashboardGroup> DeleteCategoryCommand { get; }

    public IAsyncRelayCommand ClearRecentCommand { get; }

    /// <summary>
    /// The tile the context menu was opened on.
    /// </summary>
    /// <remarks>
    /// GitExtensions works the same way (<c>_rightClickedItem</c> / <c>GetSelectedRepository</c>):
    /// the menu items act on the tile the menu belongs to, not on a parameter.
    /// </remarks>
    public DashboardRepositoryItem? SelectedItem
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            BuildCategoryChoices();

            ShowInFolderCommand.NotifyCanExecuteChanged();
            RemoveFromListCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The text in the search box.</summary>
    /// <remarks>
    /// Filtering does <b>not</b> re-read the store — GitExtensions passes
    /// <c>reloadData: false</c> for exactly this reason: reading the file system on every
    /// keystroke would make typing stutter.
    /// </remarks>
    public string SearchText
    {
        get;
        set
        {
            if (string.Equals(field, value, StringComparison.Ordinal))
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            Rebuild();
        }
    } = string.Empty;

    /// <summary>Is there anything at all to show (before filtering)?</summary>
    public bool HasRepositories => _repositories.Count > 0;

    /// <summary>Did the search leave nothing on screen?</summary>
    public bool HasResults => Groups.Count > 0;

    /// <summary>Is there an entry whose path is no longer a repository?</summary>
    public bool HasInvalidRepositories { get; private set; }

    /// <summary>The first tile on screen — what Enter in the search box opens.</summary>
    public DashboardRepositoryItem? FirstItem =>
        Groups.SelectMany(g => g.Items).FirstOrDefault();

    /// <summary>Re-reads the list from the store and rebuilds the groups.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _repositories = await _store.LoadAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable list is an empty list; the dashboard still has to come up.
            _repositories = [];
        }

        Rebuild();
    }

    /// <summary>
    /// Rebuilds the groups from the current list and the search text.
    /// </summary>
    private void Rebuild()
    {
        Groups.Clear();

        List<DashboardRepositoryItem> items = [];
        bool anyInvalid = false;

        foreach (RecentRepository repository in _repositories)
        {
            if (!Matches(repository))
            {
                continue;
            }

            DashboardRepositoryItem item = new(this, repository, Probe(repository.Path));

            anyInvalid |= !item.IsValid;
            items.Add(item);
        }

        // GitExtensions puts the recent group first and the categories after it, in alphabetical
        // order — the order of the groups must not shuffle as repositories are opened.
        List<DashboardRepositoryItem> recent = [.. items.Where(i => !i.IsFavourite)];

        if (recent.Count > 0)
        {
            Groups.Add(new DashboardGroup(Loc.T("dashboard.recent_repositories"), isRecent: true, recent));
        }

        foreach (IGrouping<string, DashboardRepositoryItem> group in items
                     .Where(i => i.IsFavourite)
                     .GroupBy(i => i.Category!, StringComparer.CurrentCulture)
                     .OrderBy(g => g.Key, StringComparer.CurrentCulture))
        {
            Groups.Add(new DashboardGroup(group.Key, isRecent: false, [.. group]));
        }

        HasInvalidRepositories = anyInvalid;

        Categories.Clear();

        foreach (string category in _repositories
                     .Where(r => r.IsFavourite)
                     .Select(r => r.Category!)
                     .Distinct(StringComparer.CurrentCulture)
                     .OrderBy(c => c, StringComparer.CurrentCulture))
        {
            Categories.Add(category);
        }

        BuildCategoryChoices();

        OnPropertyChanged(nameof(HasRepositories));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasInvalidRepositories));
        OnPropertyChanged(nameof(FirstItem));

        RemoveMissingCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Does the entry match the search text?
    /// </summary>
    /// <remarks>
    /// Both the folder name and the full path are searched: the user may remember either
    /// ("gitext" or "PrivateProjects").
    /// </remarks>
    private bool Matches(RecentRepository repository)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return repository.Path.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the "Categories" submenu for the selected tile.
    /// </summary>
    /// <remarks>
    /// The order is GitExtensions' (<c>tsmiCategories_DropDownOpening</c>): <c>(none)</c>, the
    /// existing categories, a separator, then "Add new…". The entry the repository is already in
    /// is disabled — including <c>(none)</c> for a repository that has no category.
    /// </remarks>
    private void BuildCategoryChoices()
    {
        CategoryChoices.Clear();

        DashboardRepositoryItem? selected = SelectedItem;

        CategoryChoices.Add(new DashboardCategoryChoice(
            Loc.T("dashboard.none"),
            SetCategoryCommand,
            parameter: null,
            isEnabled: selected?.IsFavourite == true));

        foreach (string category in Categories)
        {
            CategoryChoices.Add(new DashboardCategoryChoice(
                category,
                SetCategoryCommand,
                category,
                isEnabled: !string.Equals(selected?.Category, category, StringComparison.CurrentCulture)));
        }

        CategoryChoices.Add(new DashboardCategoryChoice(
            Loc.T("dashboard.add_new"),
            AddCategoryCommand,
            parameter: null,
            isEnabled: true));
    }

    private void ShowInFolder()
    {
        if (SelectedItem is { } item)
        {
            OpenFolderInShell?.Invoke(item.Path);
        }
    }

    private async Task RemoveFromListAsync()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        await _store.RemoveAsync(item.Path).ConfigureAwait(true);
        SelectedItem = null;
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Removes the entries that are no longer repositories.
    /// </summary>
    /// <remarks>
    /// Deliberately does <b>not</b> ask: nothing is lost that the user cannot get back by opening
    /// the folder again, and GitExtensions does not ask either.
    /// </remarks>
    private async Task RemoveMissingAsync()
    {
        foreach (RecentRepository repository in _repositories.Where(r => !Probe(r.Path).IsRepository).ToList())
        {
            await _store.RemoveAsync(repository.Path).ConfigureAwait(true);
        }

        SelectedItem = null;
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task SetCategoryAsync(string? category)
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        await _store.SetCategoryAsync(item.Path, category).ConfigureAwait(true);
        SelectedItem = null;
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task AddCategoryAsync()
    {
        if (SelectedItem is not { } item || Prompt is null)
        {
            return;
        }

        string? name = await Prompt.AskCategoryNameAsync([.. Categories], currentName: null)
            .ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _store.SetCategoryAsync(item.Path, name).ConfigureAwait(true);
        SelectedItem = null;
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task RenameCategoryAsync(DashboardGroup? group)
    {
        if (group is null || group.IsRecent || Prompt is null)
        {
            return;
        }

        // The name being renamed is not offered as "already taken" — otherwise renaming a
        // category to a different capitalisation of itself would be refused.
        List<string> others = [.. Categories.Where(c => !string.Equals(c, group.Header, StringComparison.CurrentCulture))];

        string? name = await Prompt.AskCategoryNameAsync(others, group.Header).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, group.Header, StringComparison.CurrentCulture))
        {
            return;
        }

        await MoveCategoryAsync(group.Header, name).ConfigureAwait(true);
    }

    private async Task DeleteCategoryAsync(DashboardGroup? group)
    {
        if (group is null || group.IsRecent || Prompt is null)
        {
            return;
        }

        bool confirmed = await Prompt.ConfirmAsync(
                Loc.T("dashboard.delete_category"),
                Loc.F("dashboard.delete_category_question", group.Header, group.Items.Count))
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        // 🔴 Deleting a category does NOT delete the repositories: they fall back into "Recent
        // repositories". Deleting the entries with the category would be a much bigger loss than
        // the user asked for — GitExtensions only clears the category too.
        await MoveCategoryAsync(group.Header, null).ConfigureAwait(true);
    }

    private async Task MoveCategoryAsync(string category, string? newCategory)
    {
        foreach (RecentRepository repository in _repositories
                     .Where(r => string.Equals(r.Category, category, StringComparison.CurrentCulture))
                     .ToList())
        {
            await _store.SetCategoryAsync(repository.Path, newCategory).ConfigureAwait(true);
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private async Task ClearRecentAsync()
    {
        if (Prompt is null)
        {
            return;
        }

        List<RecentRepository> recent = [.. _repositories.Where(r => !r.IsFavourite)];

        bool confirmed = await Prompt.ConfirmAsync(
                Loc.T("dashboard.clear_recent_repositories"),
                Loc.F("dashboard.clear_recent_repositories_question", recent.Count))
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        // The favourites stay: "clear the recent list" is not "throw away my filing".
        foreach (RecentRepository repository in recent)
        {
            await _store.RemoveAsync(repository.Path).ConfigureAwait(true);
        }

        SelectedItem = null;
        await LoadAsync().ConfigureAwait(true);
    }
}
