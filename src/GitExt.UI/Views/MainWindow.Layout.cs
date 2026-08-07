using Avalonia;
using Avalonia.Controls;
using Avalonia.Reactive;
using GitExt.UI.Settings;

namespace GitExt.UI.Views;

/// <summary>
/// Panel düzeni ve oturum kalıcılığı (P08-T13, P08-T16).
/// </summary>
public partial class MainWindow
{
    private ISettingsStore? _settings;

    /// <summary>Ayarları uygularken tetiklenen değişiklikleri geri yazmamak için.</summary>
    private bool _applyingLayout;

    /// <summary>
    /// Kaydedilmiş düzeni uygular ve değişiklikleri kaydetmeye başlar.
    /// </summary>
    /// <remarks>
    /// Pencere <b>gösterilmeden önce</b> çağrılmalı: sonrasına kalsaydı uygulama önce
    /// varsayılan boyutlarla açılıp gözle görülür biçimde yeniden yerleşirdi.
    /// </remarks>
    public void AttachLayout(ISettingsStore settings)
    {
        _settings = settings;

        ApplyStoredLayout();

        // 🔴 Ayırıcı sürüklemesi saniyede onlarca değişiklik üretiyor. Kayıt gecikmeli
        // (SettingsStore.DefaultSaveDelay) olduğu için her biri diske yazılmıyor; burada
        // yalnızca son değer bildiriliyor.
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

    /// <summary>Sol paneli açar/kapatır (<c>Ctrl+Alt+C</c>).</summary>
    public void ToggleBranchPanel() => SetBranchPanelVisible(!BranchPanelHost.IsVisible);

    /// <summary>Alt paneli açar/kapatır (<c>Ctrl+Alt+D</c>).</summary>
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

            // Sıfır ya da saçma değerler yok sayılıyor: elle düzenlenmiş bir ayar dosyası
            // paneli GÖRÜNMEZ genişliğe düşürebilir ve kullanıcı onu geri getirecek tutamağı
            // bulamazdı.
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

    private void SetBranchPanelVisible(bool visible)
    {
        BranchPanelHost.IsVisible = visible;
        BranchPanelSplitter.IsVisible = visible;

        // Sütun gizlenirken GENİŞLİĞİ de sıfırlanmalı: yalnızca `IsVisible` kapatmak,
        // sütunu 220 piksellik BOŞ bir şerit olarak bırakırdı.
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

            // Büyütülmüş pencerenin boyutunu kaydetmiyoruz: geri alındığında kullanıcı
            // ekranı kaplayan bir "normal" pencereyle kalırdı.
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
