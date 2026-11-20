using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using ChatHub.Services;
using ChatHub.ViewModels;

namespace ChatHub.Views;

/// <summary>
/// 关于窗口：展示项目地址与当前版本，调用 upgrade.bat check / upgrade 完成更新。
/// 服务在此处注入，视图模型只负责状态与流程，便于单元测试。
/// </summary>
public partial class AboutWindow : Window
{
    private readonly AboutViewModel _viewModel;

    public AboutWindow(string currentVersion)
    {
        InitializeComponent();

        var runner = new UpgradeScriptRunner();
        var downloader = new UpdateDownloader();

        _viewModel = new AboutViewModel(currentVersion)
        {
            CheckRelease = cancellationToken => runner.CheckAsync(cancellationToken),
            DownloadRelease = (release, progress, cancellationToken) =>
                downloader.DownloadAsync(release, progress, cancellationToken),
            StartUpgrade = zipPath => runner.StartUpgrade(zipPath),
            ShutdownApplication = () =>
            {
                if (Application.Current is { } app)
                {
                    app.Shutdown();
                }
            },
            RequestClose = Close,
        };

        DataContext = _viewModel;
        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsChecking || _viewModel.IsDownloading)
        {
            // 关闭窗口即取消尚未完成的网络请求；.part 半成品由下载服务清理。
            _viewModel.Cancel();
        }
    }

    private void OnProjectLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开链接失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        e.Handled = true;
    }
}
