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
    private SiteManagerWindow? _siteManagerWindow;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();

        var store = new SiteStore();
        var viewModel = new MainWindowViewModel(store);
        var pool = new WebViewPool(viewModel, RetireArea);
        viewModel.AttachPool(pool);

        _viewModel = viewModel;
        _pool = pool;

        // 站点切换由视图模型通知，视图负责把对应控件挂到内容区。
        _viewModel.SiteActivated += OnSiteActivated;

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
        // 池返回已缓存实例时不会重新导航，切回提示由视图模型写入状态栏。
        ContentHost.Content = _pool.GetOrCreate(site);
    }

    private void OnSiteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SiteStrip.SelectedItem as Site;

        if (selected is not null && !ReferenceEquals(selected, _viewModel.CurrentSite))
        {
            _viewModel.SelectSiteCommand.Execute(selected);
        }

        // 当前站点被删除后：重新载入清单并落回第一个可用站点，避免内容区停留在已删除的页面。
        if (SiteStrip.SelectedItem is null && _viewModel.CurrentSite is null && _viewModel.Sites.Count > 0)
        {
            _viewModel.ReloadSites();
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
        ContentHost.Content = null;
        _siteManagerWindow?.Close();
        _viewModel.Dispose();
    }
}
