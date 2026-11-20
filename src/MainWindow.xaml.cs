using System.ComponentModel;
using System.Diagnostics;
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
    private SiteManagerWindow? _siteManagerWindow;
    private AboutWindow? _aboutWindow;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();

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
        _viewModel.SiteActivated -= OnSiteActivated;
        _viewModel.TabActivated -= OnTabActivated;
        _viewModel.ContentCleared -= OnContentCleared;
        ContentHost.Content = null;
        _siteManagerWindow?.Close();
        _aboutWindow?.Close();
        _viewModel.Dispose();
    }
}
