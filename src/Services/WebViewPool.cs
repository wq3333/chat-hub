using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ChatHub.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatHub.Services;

/// <summary>池向界面回调导航状态的通道，避免服务层直接依赖窗口类型。</summary>
public interface IWebViewHostFeedback
{
    /// <summary>状态文本（加载中、错误、提示等）。</summary>
    void OnStatus(string message);

    /// <summary>进度：loading=false 时结束进度显示。</summary>
    void OnProgress(bool loading, double percent);

    /// <summary>当前地址变化。</summary>
    void OnAddressChanged(string url);

    /// <summary>前进/后退可用性可能变化。</summary>
    void OnHistoryChanged();

    /// <summary>WebView2 无法初始化（通常是未安装运行时）。</summary>
    void OnInitializationFailed(string message);
}

/// <summary>
/// 每个站点一个 WebView2，并按站点 id 缓存。
/// 切换站点时只做视觉挂载/卸载，不销毁实例，因此各站点的页面与会话（登录态）都能保留。
/// 所有实例共用同一个 WebView2 用户数据目录，Cookie 与登录态在站点间一致，重启后依然有效。
/// </summary>
public sealed class WebViewPool : IDisposable
{
    /// <summary>WebView2 运行时缺失时的官方下载入口（Evergreen Bootstrapper）。</summary>
    public const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private readonly Dictionary<string, WebView2> _views = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>每站点的 CoreWebView2 就绪任务：控件挂到窗口上之后才真正开始初始化。</summary>
    private readonly Dictionary<string, Task<CoreWebView2?>> _readyCores = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _userDataFolder;
    private readonly IWebViewHostFeedback _feedback;
    private readonly Panel _retireContainer;
    private bool _runtimeFailureReported;
    private bool _disposed;

    public WebViewPool(IWebViewHostFeedback feedback, Panel retireContainer, string? userDataFolder = null)
    {
        _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        _retireContainer = retireContainer ?? throw new ArgumentNullException(nameof(retireContainer));
        _userDataFolder = string.IsNullOrWhiteSpace(userDataFolder) ? AppPaths.WebView2UserDataFolder : userDataFolder;
    }

    /// <summary>当前选中站点的 WebView2（由界面在切换时告知）。</summary>
    public WebView2? CurrentView { get; private set; }

    public bool CanGoBack => CurrentCore?.CanGoBack ?? false;

    public bool CanGoForward => CurrentCore?.CanGoForward ?? false;

    private CoreWebView2? CurrentCore => CurrentView?.CoreWebView2;

    public void SetCurrent(WebView2? view)
    {
        CurrentView = view;
        _feedback.OnHistoryChanged();
    }

    /// <summary>取站点对应的 WebView2；不存在则新建。返回 null 表示池已释放。</summary>
    public WebView2? GetOrCreate(Site site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (_disposed)
        {
            return null;
        }

        if (_views.TryGetValue(site.Id, out var existing))
        {
            return existing;
        }

        var view = CreateView(site);
        _views[site.Id] = view;
        return view;
    }

    /// <summary>某站点是否已经创建过页面。</summary>
    public bool IsCached(string siteId) => _views.ContainsKey(siteId);

    /// <summary>
    /// 确保页面已就绪并处于该站点的网址上。
    /// 控件必须先被挂到窗口上（可见树）才会初始化，所以这里会先等挂载完成。
    /// 已经就绪且已在该站点地址上的页面不会被重新导航，因此会话（含聊天记录）得以保留。
    /// </summary>
    public async Task<bool> InitializeAsync(Site site)
    {
        var view = GetOrCreate(site);
        if (view is null)
        {
            return false;
        }

        var core = await EnsureReadyCoreAsync(site, view).ConfigureAwait(true);
        if (core is null)
        {
            return false;
        }

        // 站点网址被修改过（或还没加载过）时，让页面导航过去。
        if (!IsOnSite(core, site.Url))
        {
            NavigateCore(core, site.Url);
        }

        return true;
    }

    public void GoBack()
    {
        if (CurrentCore is { CanGoBack: true } core)
        {
            core.GoBack();
        }
    }

    public void GoForward()
    {
        if (CurrentCore is { CanGoForward: true } core)
        {
            core.GoForward();
        }
    }

    public void Reload() => CurrentCore?.Reload();

    /// <summary>导航到指定网址（界面层已校验，这里仍做一次兜底）。</summary>
    public void Navigate(string url)
    {
        if (CurrentCore is { } core)
        {
            NavigateCore(core, url);
        }
    }

    /// <summary>释放某站点的页面（删除站点时调用），结束它对应的渲染进程。</summary>
    public void Remove(string siteId)
    {
        _readyCores.Remove(siteId);

        if (!_views.Remove(siteId, out var view))
        {
            return;
        }

        if (ReferenceEquals(CurrentView, view))
        {
            CurrentView = null;
        }

        Retire(view);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var view in _views.Values)
        {
            Retire(view);
        }

        _views.Clear();
        _readyCores.Clear();
        CurrentView = null;
    }

    /// <summary>
    /// 等待控件挂载完成 → 初始化 CoreWebView2 → 完成一次性配置。
    /// 同一站点只会真正初始化一次，后续调用直接复用已就绪的核心。
    /// </summary>
    private Task<CoreWebView2?> EnsureReadyCoreAsync(Site site, WebView2 view)
    {
        if (view.CoreWebView2 is { } already)
        {
            return Task.FromResult<CoreWebView2?>(already);
        }

        if (_readyCores.TryGetValue(site.Id, out var pending))
        {
            return pending;
        }

        var task = CreateReadyCoreAsync(site, view);
        _readyCores[site.Id] = task;
        return task;
    }

    private async Task<CoreWebView2?> CreateReadyCoreAsync(Site site, WebView2 view)
    {
        try
        {
            // 关键：WebView2 只有在进入可视树之后才能完成初始化。
            // 之前直接把控件交给内容区就立刻初始化，若此时尚未完成布局就会失败并静默放弃导航，
            // 表现为「点击站点按钮没有反应」。
            await WaitForMountedAsync(view).ConfigureAwait(true);

            await view.EnsureCoreWebView2Async().ConfigureAwait(true);

            var core = view.CoreWebView2;
            if (core is null)
            {
                _feedback.OnStatus($"「{site.Name}」的页面组件初始化失败，可点击「刷新」重试。");
                return null;
            }

            ConfigureCore(core, view);
            return core;
        }
        catch (Exception ex)
        {
            _readyCores.Remove(site.Id);
            ReportRuntimeFailure(ex);
            return null;
        }
    }

    private static Task WaitForMountedAsync(WebView2 view)
    {
        if (view.IsLoaded)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            completion.TrySetResult(true);
        }

        view.Loaded += OnLoaded;
        completion.Task.ContinueWith(
            _ => view.Dispatcher.Invoke(() => view.Loaded -= OnLoaded),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // 兜底：极端情况下 Loaded 迟迟不来，也要让调用方拿到明确结果而不是一直等待。
        return WaitWithTimeoutAsync(completion.Task, TimeSpan.FromSeconds(15));
    }

    private static async Task WaitWithTimeoutAsync(Task task, TimeSpan timeout)
    {
        var delay = Task.Delay(timeout);
        if (await Task.WhenAny(task, delay).ConfigureAwait(true) == delay)
        {
            return;
        }

        await task.ConfigureAwait(true);
    }

    /// <summary>切换站点时，先让控件进入可视树，再回来初始化。</summary>
    private WebView2 CreateView(Site site)
    {
        var view = new WebView2
        {
            // 用 CreationProperties 固定用户数据目录，避免 WebView2 隐式初始化时落到默认位置。
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = _userDataFolder,
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            DefaultBackgroundColor = System.Drawing.Color.White,
        };

        view.NavigationStarting += (_, _) =>
        {
            if (ReferenceEquals(view, CurrentView))
            {
                _feedback.OnStatus("正在加载…");
                _feedback.OnProgress(true, 0);
            }
        };

        view.NavigationCompleted += (_, e) => OnNavigationCompleted(view, e);

        return view;
    }

    private void ConfigureCore(CoreWebView2 core, WebView2 view)
    {
        var settings = core.Settings;
        settings.IsStatusBarEnabled = false;
        settings.AreDefaultContextMenusEnabled = true;
        settings.AreBrowserAcceleratorKeysEnabled = true;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsZoomControlEnabled = true;

        // 只屏蔽通知权限弹窗：语音类站点常用麦克风/摄像头，保持 WebView2 默认行为以免误伤。
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
            {
                e.State = CoreWebView2PermissionState.Deny;
                e.Handled = true;
            }
        };

        core.NewWindowRequested += (_, e) =>
        {
            // 站点的外部链接交给系统默认浏览器，应用内只保留当前站点页面。
            e.Handled = true;
            OpenInSystemBrowser(e.Uri);
        };

        core.ProcessFailed += (_, e) =>
        {
            _feedback.OnStatus($"页面进程异常（{e.ProcessFailedKind}），可点击「刷新」重试。");
            _feedback.OnProgress(false, 0);
        };

        // 状态回调只汇报当前选中站点，避免后台站点把状态栏刷成它的信息。
        core.HistoryChanged += (_, _) =>
        {
            if (ReferenceEquals(view, CurrentView))
            {
                _feedback.OnHistoryChanged();
            }
        };

        core.SourceChanged += (_, _) =>
        {
            if (ReferenceEquals(view, CurrentView))
            {
                _feedback.OnAddressChanged(core.Source);
            }
        };
    }

    private void OnNavigationCompleted(WebView2 view, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!ReferenceEquals(view, CurrentView))
        {
            return;
        }

        _feedback.OnProgress(false, 100);
        _feedback.OnHistoryChanged();

        if (e.IsSuccess)
        {
            _feedback.OnStatus("加载完成");
            return;
        }

        var reason = e.WebErrorStatus switch
        {
            CoreWebView2WebErrorStatus.HostNameNotResolved => "无法解析域名",
            CoreWebView2WebErrorStatus.ConnectionAborted or CoreWebView2WebErrorStatus.ConnectionReset => "网络连接被中断",
            CoreWebView2WebErrorStatus.Timeout => "连接超时",
            CoreWebView2WebErrorStatus.Disconnected => "网络连接不可用",
            _ => e.WebErrorStatus.ToString(),
        };
        _feedback.OnStatus($"加载失败：{reason}（可点击「刷新」重试）");
    }

    private static void NavigateCore(CoreWebView2 core, string url)
    {
        var normalized = UrlNormalizer.Normalize(url);
        if (normalized is not null)
        {
            core.Navigate(normalized);
        }
    }

    private void ReportRuntimeFailure(Exception? exception)
    {
        if (_runtimeFailureReported)
        {
            return;
        }

        _runtimeFailureReported = true;
        _feedback.OnProgress(false, 0);
        var detail = exception is null ? string.Empty : $"\n错误信息：{exception.Message}";
        _feedback.OnInitializationFailed(
            "无法初始化 WebView2 渲染组件，通常是未安装「Microsoft Edge WebView2 Runtime」。" + detail);
    }

    private void Retire(WebView2 view)
    {
        DetachFromParent(view);
        try
        {
            // 先挂到不可见的退休容器上再 Dispose，避免窗口句柄仍在视觉树中引发释放异常。
            _retireContainer.Children.Add(view);
            view.Dispose();
        }
        catch (Exception)
        {
            // 释放失败不影响主流程，进程会随应用退出结束。
        }

        _retireContainer.Children.Remove(view);
    }

    /// <summary>判断页面当前是否已经在该站点地址上（用于决定要不要重新导航）。</summary>
    private static bool IsOnSite(CoreWebView2 core, string url)
    {
        var current = core.Source;
        if (string.IsNullOrEmpty(current) || current == "about:blank")
        {
            return false;
        }

        return UrlNormalizer.AreEquivalent(current, url);
    }

    private static void DetachFromParent(WebView2 view)
    {
        switch (view.Parent)
        {
            case Panel panel:
                panel.Children.Remove(view);
                break;
            case ContentControl content when ReferenceEquals(content.Content, view):
                content.Content = null;
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, view):
                decorator.Child = null;
                break;
            case Window window when ReferenceEquals(window.Content, view):
                window.Content = null;
                break;
        }
    }

    private static void OpenInSystemBrowser(string? url)
    {
        var normalized = UrlNormalizer.Normalize(url);
        if (normalized is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(normalized) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 没有可用浏览器时静默忽略，不影响应用本身。
        }
    }
}
