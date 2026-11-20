using ChatHub.Services;

namespace ChatHub.ViewModels;

/// <summary>
/// 关于窗口的状态机：检查更新（调用 upgrade.bat check）、展示新版本信息、
/// 下载更新包并上报进度，最后交给 upgrade.bat upgrade 并关闭应用。
/// 不做任何 IO：网络与进程调用由窗口注入，便于测试。
/// </summary>
public sealed class AboutViewModel : ObservableObject
{
    private readonly CancellationTokenSource _cancellation = new();
    private string _statusText = "点击「检查更新」查看是否有新版本。";
    private bool _isChecking;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _downloadStatusText = string.Empty;
    private ReleaseInfo? _latest;

    public AboutViewModel(string currentVersion)
    {
        CurrentVersion = string.IsNullOrWhiteSpace(currentVersion) ? "0.0.0" : currentVersion.Trim();
        CheckCommand = new RelayCommand(CheckForUpdates, () => !IsBusy);
        UpdateCommand = new RelayCommand(DownloadAndUpdate, () => HasUpdate && !IsBusy);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    public string CurrentVersion { get; }

    public string ProjectUrl => AppInfo.RepositoryUrl;

    public RelayCommand CheckCommand { get; }

    public RelayCommand UpdateCommand { get; }

    public RelayCommand CloseCommand { get; }

    /// <summary>由窗口注入：执行升级脚本的 check。</summary>
    public Func<CancellationToken, Task<UpdateCheckResult>>? CheckRelease { get; set; }

    /// <summary>由窗口注入：下载 release 资产并返回本地 zip 路径。</summary>
    public Func<ReleaseInfo, IProgress<DownloadProgress>, CancellationToken, Task<string>>? DownloadRelease { get; set; }

    /// <summary>由窗口注入：启动 upgrade.bat upgrade。</summary>
    public Action<string>? StartUpgrade { get; set; }

    /// <summary>由窗口注入：更新已交接给升级脚本后关闭应用。</summary>
    public Action? ShutdownApplication { get; set; }

    public Action? RequestClose { get; set; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                OnBusyChanged();
            }
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                OnBusyChanged();
            }
        }
    }

    public bool IsBusy => IsChecking || IsDownloading;

    public double DownloadProgress
    {
        get => _downloadProgress;
        private set => SetProperty(ref _downloadProgress, value);
    }

    public string DownloadStatusText
    {
        get => _downloadStatusText;
        private set => SetProperty(ref _downloadStatusText, value);
    }

    public ReleaseInfo? Latest => _latest;

    public bool HasLatestRelease => _latest is not null;

    /// <summary>只有发布版本严格高于当前版本时才提供更新入口。</summary>
    public bool HasUpdate => _latest is not null && UpdateChecker.IsNewer(_latest.Version, CurrentVersion);

    public string LatestVersionText =>
        _latest is null ? string.Empty : "v" + UpdateChecker.NormalizeTag(_latest.Version);

    public string LatestPublishedText =>
        _latest?.PublishedAt is { } published
            ? published.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "未知";

    public string LatestNotes => _latest?.Notes ?? string.Empty;

    public string LatestPageUrl => _latest?.PageUrl ?? string.Empty;

    /// <summary>窗口关闭时取消仍在进行的检查或下载。</summary>
    public void Cancel() => _cancellation.Cancel();

    /// <summary>检查更新；异常与失败都转换成状态文本，不向调用方抛出。</summary>
    public async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (CheckRelease is null)
        {
            StatusText = "未配置更新检查。";
            return;
        }

        IsChecking = true;
        DownloadProgress = 0;
        DownloadStatusText = string.Empty;
        StatusText = "正在检查更新…";

        try
        {
            var result = await CheckRelease(_cancellation.Token).ConfigureAwait(true);
            if (!result.Success || result.Release is null)
            {
                SetLatest(null);
                StatusText = $"检查更新失败：{result.Error}";
                return;
            }

            SetLatest(result.Release);

            var comparison = UpdateChecker.Compare(result.Release.Version, CurrentVersion);
            if (comparison is null)
            {
                StatusText = $"无法比较版本：发布版本 {LatestVersionText}，当前版本 {CurrentVersion}。";
            }
            else if (comparison.Value > 0)
            {
                StatusText = $"发现新版本 {LatestVersionText}（当前 {CurrentVersion}）。";
            }
            else if (comparison.Value == 0)
            {
                StatusText = $"已是最新版本（{CurrentVersion}）。";
            }
            else
            {
                StatusText = $"当前版本 {CurrentVersion} 比发布版本 {LatestVersionText} 更新，无需更新。";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消检查更新。";
        }
        catch (Exception ex)
        {
            SetLatest(null);
            StatusText = $"检查更新失败：{ex.Message}";
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>下载更新包并在成功后交接给 upgrade.bat；失败时停留在窗口内显示原因。</summary>
    public async Task DownloadAndUpdateAsync()
    {
        if (!HasUpdate || IsBusy || _latest is null)
        {
            return;
        }

        if (DownloadRelease is null)
        {
            StatusText = "未配置更新下载。";
            return;
        }

        if (StartUpgrade is null)
        {
            StatusText = "未配置升级脚本。";
            return;
        }

        var release = _latest;
        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatusText = "准备下载…";
        StatusText = $"正在下载 {LatestVersionText}…";

        try
        {
            var progress = new Progress<DownloadProgress>(OnDownloadProgress);
            var zipPath = await DownloadRelease(release, progress, _cancellation.Token).ConfigureAwait(true);

            DownloadProgress = 100;
            DownloadStatusText = "下载完成。";
            StatusText = "下载完成，正在重启并应用更新…";

            StartUpgrade?.Invoke(zipPath);
            ShutdownApplication?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消下载更新。";
            DownloadStatusText = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = $"更新失败：{ex.Message}";
            DownloadStatusText = string.Empty;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private void CheckForUpdates() => _ = CheckAsync();

    private void DownloadAndUpdate() => _ = DownloadAndUpdateAsync();

    private void OnDownloadProgress(DownloadProgress progress)
    {
        if (progress.TotalBytes is > 0)
        {
            // 用向下取整：99.6% 仍显示 99%，避免尚未收尾就跳到 100%。
            DownloadProgress = Math.Floor(progress.Percent ?? DownloadProgress);
            DownloadStatusText =
                $"{FormatBytes(progress.ReceivedBytes)} / {FormatBytes(progress.TotalBytes.Value)}" +
                $"（{Math.Floor(progress.Percent!.Value)}%）";
        }
        else
        {
            DownloadStatusText = FormatBytes(progress.ReceivedBytes);
        }
    }

    private void SetLatest(ReleaseInfo? release)
    {
        _latest = release;

        OnPropertyChanged(nameof(Latest));
        OnPropertyChanged(nameof(HasLatestRelease));
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(LatestVersionText));
        OnPropertyChanged(nameof(LatestPublishedText));
        OnPropertyChanged(nameof(LatestNotes));
        OnPropertyChanged(nameof(LatestPageUrl));
        UpdateCommand.RaiseCanExecuteChanged();
    }

    private void OnBusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        CheckCommand.RaiseCanExecuteChanged();
        UpdateCommand.RaiseCanExecuteChanged();
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024.0):0.0} MB"
            : $"{bytes / 1024.0:0} KB";
}
