using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ChatHub.Models;
using ChatHub.Services;
using ChatHub.ViewModels;
using ChatHub.Views;

namespace ChatHub;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly WebViewPool _pool;
    private readonly InAppTabHost _tabHost;
    private readonly WindowPlacementStore _windowPlacementStore = new();
    private SiteManagerWindow? _siteManagerWindow;
    private AboutWindow? _aboutWindow;
    private WindowState _lastNonMinimizedState = WindowState.Normal;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();

        // 先决定初始窗口状态：有有效记录就恢复，没有就默认最大化（全屏）。
        ApplySavedWindowPlacement();
        StateChanged += OnWindowStateChanged;

        var store = new SiteStore();
        var viewModel = new MainWindowViewModel(store);
        var runtime = new WebViewRuntime(viewModel, RetireArea);
        var tabHost = new InAppTabHost(runtime);
        runtime.NewWindowRequestedHandler = tabHost.HandleNewWindowRequested;

        var pool = new WebViewPool(runtime);
        viewModel.AttachRuntime(runtime);
        viewModel.AttachPool(pool);
        viewModel.AttachTabHost(tabHost);

        _viewModel = viewModel;
        _pool = pool;
        _tabHost = tabHost;

        // 站点切换与临时标签页切换都由视图模型通知，视图负责把对应控件挂到内容区。
        _viewModel.SiteActivated += OnSiteActivated;
        _viewModel.TabActivated += OnTabActivated;
        _viewModel.ContentCleared += OnContentCleared;

        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnWindowClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private void OnSiteActivated(Site site)
    {
        if (_disposed)
        {
            return;
        }

        // 池返回已缓存实例时不会重新导航，切回提示由视图模型写入状态栏。
        ContentHost.Content = _pool.GetOrCreate(site);
    }

    private void OnTabActivated(InAppTab tab)
    {
        if (_disposed)
        {
            return;
        }

        // 新窗口链接先挂载视图，随后标签宿主才异步初始化；不能反过来。
        ContentHost.Content = _tabHost.GetView(tab);
    }

    private void OnContentCleared()
    {
        if (_disposed)
        {
            return;
        }

        ContentHost.Content = null;
    }

    private void OnSiteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SiteStrip.SelectedItem as Site;

        if (selected is not null)
        {
            // 标签页激活时站点条目会被取消选中；此时点击当前站点也应回到站点页。
            if (_viewModel.ActiveTab is not null || !ReferenceEquals(selected, _viewModel.CurrentSite))
            {
                _viewModel.ActivateSite(selected);
            }
        }

        // 当前站点被删除后：重新载入清单并落回第一个可用站点，避免内容区停留在已删除的页面。
        if (SiteStrip.SelectedItem is null && _viewModel.CurrentSite is null && _viewModel.Sites.Count > 0)
        {
            _viewModel.ReloadSites();
        }
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (TabStrip.SelectedItem is InAppTab tab && !ReferenceEquals(tab, _viewModel.ActiveTab))
        {
            _viewModel.ActivateTab(tab);
        }
    }

    private void OnManageSitesClick(object sender, RoutedEventArgs e)
    {
        if (_siteManagerWindow is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }

        var window = new SiteManagerWindow(_viewModel) { Owner = this };
        _siteManagerWindow = window;
        window.ShowDialog();
        _siteManagerWindow = null;

        // 管理窗口里的增删改已即时落盘，这里同步工具栏与内容区。
        _viewModel.ReloadSites();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        if (_aboutWindow is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }

        var window = new AboutWindow(AppInfo.CurrentVersion) { Owner = this };
        _aboutWindow = window;
        window.ShowDialog();
        _aboutWindow = null;
    }

    private void OnDownloadRuntimeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(WebViewPool.RuntimeDownloadUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开下载页失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCopyRuntimeUrlClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(WebViewPool.RuntimeDownloadUrl);
            _viewModel.OnStatus("已复制 WebView2 运行时下载地址。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "复制失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SaveWindowPlacement();
        StateChanged -= OnWindowStateChanged;
        _viewModel.SiteActivated -= OnSiteActivated;
        _viewModel.TabActivated -= OnTabActivated;
        _viewModel.ContentCleared -= OnContentCleared;
        ContentHost.Content = null;
        _siteManagerWindow?.Close();
        _aboutWindow?.Close();
        _viewModel.Dispose();
    }

    /// <summary>
    /// 恢复上次的窗口位置/大小；没有记录、记录损坏、或上次的显示器已不在时，默认最大化。
    /// </summary>
    private void ApplySavedWindowPlacement()
    {
        var placement = _windowPlacementStore.Load();
        if (placement is null)
        {
            MaximizeByDefault();
            return;
        }

        // VirtualScreen 覆盖所有显示器，窗口可以跨屏或位于副屏。
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenWidth = SystemParameters.VirtualScreenWidth;
        var screenHeight = SystemParameters.VirtualScreenHeight;

        if (!placement.IsOnScreen(screenLeft, screenTop, screenWidth, screenHeight))
        {
            // 例如上次在副屏，本次副屏被拔掉：不要显示到看不见的地方。
            MaximizeByDefault();
            return;
        }

        var clamped = placement.ClampInto(screenLeft, screenTop, screenWidth, screenHeight);

        // 必须先设 Manual，否则 CenterScreen 会覆盖掉恢复的位置。
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = clamped.Left;
        Top = clamped.Top;
        Width = Math.Max(clamped.Width, MinWidth);
        Height = Math.Max(clamped.Height, MinHeight);
        WindowState = clamped.IsMaximized ? WindowState.Maximized : WindowState.Normal;
        _lastNonMinimizedState = WindowState;
    }

    private void MaximizeByDefault()
    {
        WindowState = WindowState.Maximized;
        _lastNonMinimizedState = WindowState.Maximized;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // 最小化时保留最小化之前的状态，关闭时据此决定下次是否最大化。
        if (WindowState != WindowState.Minimized)
        {
            _lastNonMinimizedState = WindowState;
        }
    }

    /// <summary>关闭前记录普通尺寸/位置与最大化状态；写入失败绝不阻塞退出。</summary>
    private void SaveWindowPlacement()
    {
        try
        {
            var state = WindowState == WindowState.Minimized ? _lastNonMinimizedState : WindowState;

            // RestoreBounds 保留“最大化/最小化之前”的普通窗口尺寸，这正是下次启动需要的。
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;

            if (bounds.IsEmpty ||
                !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
                !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
                bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            _windowPlacementStore.Save(new WindowPlacement(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                state == WindowState.Maximized));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 窗口状态只是便利功能，写不了就算了。
        }
    }
}
