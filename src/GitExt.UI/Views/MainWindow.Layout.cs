using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;
using GitExt.UI.Settings;
using GitExt.UI.ViewModels;

namespace GitExt.UI.Views;

/// <summary>
/// The panel layout and session persistence (P08-T13, P08-T16).
/// </summary>
public partial class MainWindow
{
    private ISettingsStore? _settings;

    /// <summary>So the changes raised while applying the settings are not written back.</summary>
    private bool _applyingLayout;

    /// <summary>
    /// Applies the saved layout and starts saving changes.
    /// </summary>
    /// <remarks>
    /// Must be called <b>before the window is shown</b>: left until afterwards, the application would
    /// open at its default sizes first and visibly re-lay itself out.
    /// </remarks>
    public void AttachLayout(ISettingsStore settings)
    {
        _settings = settings;

        ApplyStoredLayout();

        // 🔴 Dragging a splitter produces dozens of changes a second. Because the save is delayed
        // (SettingsStore.DefaultSaveDelay) not every one of them is written to disk; only the final
        // value is reported here.
        MainSplitGrid.ColumnDefinitions[0]
            .GetObservable(ColumnDefinition.WidthProperty)
            .Subscribe(new AnonymousObserver<GridLength>(width => Persist(
                s => s.Layout.BranchPanelWidth = width.IsAbsolute ? width.Value : s.Layout.BranchPanelWidth)));

        RightSplitGrid.RowDefinitions[2]
            .GetObservable(RowDefinition.HeightProperty)
            .Subscribe(new AnonymousObserver<GridLength>(height => Persist(
                s => s.Layout.BottomPanelHeight = height.IsAbsolute ? height.Value : s.Layout.BottomPanelHeight)));

        // Pencere boyutu ve durumu (P08-T16).
        Closing += (_, _) => PersistWindow();
    }

    /// <summary>Toggles the left panel (<c>Ctrl+Alt+C</c>).</summary>
    public void ToggleBranchPanel() => SetBranchPanelVisible(!BranchPanelHost.IsVisible);

    /// <summary>Toggles the bottom panel (<c>Ctrl+Alt+D</c>).</summary>
    public void ToggleBottomPanel() => SetBottomPanelVisible(!BottomPanel.IsVisible);

    private void ApplyStoredLayout()
    {
        if (_settings is not { } settings)
        {
            return;
        }

        _applyingLayout = true;

        try
        {
            LayoutSettings layout = settings.Current.Layout;

            // Zero or nonsensical values are ignored: a hand-edited settings file could shrink the panel
            // to an INVISIBLE width and the user would not find the handle to bring it back.
            if (layout.BranchPanelWidth >= 80)
            {
                MainSplitGrid.ColumnDefinitions[0].Width = new GridLength(layout.BranchPanelWidth);
            }

            if (layout.BottomPanelHeight >= 60)
            {
                RightSplitGrid.RowDefinitions[2].Height = new GridLength(layout.BottomPanelHeight);
            }

            SetBranchPanelVisible(layout.BranchPanelVisible);
            SetBottomPanelVisible(layout.BottomPanelVisible);

            // The left panel's section toggles (P12-T13). The panel itself does not know where
            // settings live (ADR-0004): it reports a change and the window stores it.
            if (DataContext is MainWindowViewModel model)
            {
                model.RefTree.Sections = new RefTreeSections
                {
                    Branches = layout.LeftPanel.Branches,
                    Remotes = layout.LeftPanel.Remotes,
                    WorkTrees = layout.LeftPanel.WorkTrees,
                    Tags = layout.LeftPanel.Tags,
                    Submodules = layout.LeftPanel.Submodules,
                    Stashes = layout.LeftPanel.Stashes,
                };

                model.RefTree.SectionsChanged -= OnLeftPanelSectionsChanged;
                model.RefTree.SectionsChanged += OnLeftPanelSectionsChanged;
            }

            SessionSettings session = settings.Current.Session;

            if (session.WindowWidth >= MinWidth && session.WindowHeight >= MinHeight)
            {
                Width = session.WindowWidth;
                Height = session.WindowHeight;
            }

            if (session.WindowMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    private void OnLeftPanelSectionsChanged(object? sender, RefTreeSections sections) =>
        Persist(settings =>
        {
            settings.Layout.LeftPanel.Branches = sections.Branches;
            settings.Layout.LeftPanel.Remotes = sections.Remotes;
            settings.Layout.LeftPanel.WorkTrees = sections.WorkTrees;
            settings.Layout.LeftPanel.Tags = sections.Tags;
            settings.Layout.LeftPanel.Submodules = sections.Submodules;
            settings.Layout.LeftPanel.Stashes = sections.Stashes;
        });

    private void SetBranchPanelVisible(bool visible)
    {
        BranchPanelHost.IsVisible = visible;
        BranchPanelSplitter.IsVisible = visible;

        // When the column is hidden its WIDTH has to be reset too: turning off `IsVisible` alone would
        // leave the column as an EMPTY 220-pixel strip.
        MainSplitGrid.ColumnDefinitions[0].Width = visible
            ? new GridLength(StoredBranchWidth())
            : new GridLength(0);

        MainSplitGrid.ColumnDefinitions[1].Width = new GridLength(visible ? 4 : 0);

        Persist(s => s.Layout.BranchPanelVisible = visible);
    }

    private void SetBottomPanelVisible(bool visible)
    {
        BottomPanel.IsVisible = visible;
        BottomPanelSplitter.IsVisible = visible;

        RightSplitGrid.RowDefinitions[2].Height = visible
            ? new GridLength(StoredBottomHeight())
            : new GridLength(0);

        RightSplitGrid.RowDefinitions[1].Height = new GridLength(visible ? 4 : 0);

        Persist(s => s.Layout.BottomPanelVisible = visible);
    }

    private double StoredBranchWidth() =>
        _settings?.Current.Layout.BranchPanelWidth is >= 80 and var width ? width : 220;

    private double StoredBottomHeight() =>
        _settings?.Current.Layout.BottomPanelHeight is >= 60 and var height ? height : 220;

    private void PersistWindow()
    {
        bool maximized = WindowState == WindowState.Maximized;

        Persist(s =>
        {
            s.Session.WindowMaximized = maximized;

            // We do not save a maximised window's size: on restore the user would be left with a
            // "normal" window covering the screen.
            if (!maximized)
            {
                s.Session.WindowWidth = Width;
                s.Session.WindowHeight = Height;
            }
        });
    }

    private void Persist(Action<AppSettings> change)
    {
        if (_applyingLayout || _settings is not { } settings)
        {
            return;
        }

        settings.Update(change);
    }
}
