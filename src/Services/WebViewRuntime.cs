using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatHub.Services;

/// <summary>
/// 站点页与临时标签页共用的 WebView2 运行时。
///
/// 关键点：<see cref="CoreWebView2NewWindowRequestedEventArgs.NewWindow"/> 要求新页面与打开者
/// 使用同一个 <see cref="CoreWebView2Environment"/> 与同一个 Profile，因此所有 WebView2
/// 都通过这里懒创建的唯一环境初始化；否则应用内标签页无法接管新窗口，登录弹窗的
/// window.opener 关系也会丢失。
///
/// 站点缓存仍在 <see cref="WebViewPool"/>，临时标签页在 <see cref="InAppTabHost"/>，
/// 这里只负责两边共用的环境、活动视图指针与通用事件接线。
/// </summary>
public sealed class WebViewRuntime : IDisposable
{
    /// <summary>
    /// 页面脚本里的 window.close() 若直接放行，WebView2 会向托管它的主窗口发送关闭请求。
    /// 临时标签页用这个钩子关闭自己，站点页则忽略请求（站点页不是独立窗口）。
    /// </summary>
    private const string CloseWindowMessage = "chathub:close-window";

    private const string CloseWindowGuardScript = """
        (() => {
          window.close = function () {
            try {
              if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage('chathub:close-window');
              }
            } catch (e) { }
          };
        })();
        """;

    private readonly IWebViewHostFeedback _feedback;
    private readonly Panel _retireContainer;
    private readonly string _userDataFolder;
    private readonly HashSet<WebView2> _loadingViews = [];
    private Task<CoreWebView2Environment?>? _environmentTask;
    private bool _runtimeFailureReported;
    private bool _disposed;

    public WebViewRuntime(IWebViewHostFeedback feedback, Panel retireContainer, string? userDataFolder = null)
    {
        _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        _retireContainer = retireContainer ?? throw new ArgumentNullException(nameof(retireContainer));
        _userDataFolder = string.IsNullOrWhiteSpace(userDataFolder) ? AppPaths.WebView2UserDataFolder : userDataFolder;
    }

    public IWebViewHostFeedback Feedback => _feedback;

    /// <summary>当前真正显示在内容区的页面（站点页或临时标签页），工具栏命令与状态回调都以它为准。</summary>
    public WebView2? ActiveView { get; private set; }

    public CoreWebView2? ActiveCore => ActiveView?.CoreWebView2;

    public bool HasActiveView => ActiveView is not null;

    /// <summary>
    /// 内容页请求新窗口时的处理入口（由 <see cref="InAppTabHost"/> 提供）。
    /// 未设置时退回系统默认浏览器，保持旧行为。
    /// </summary>
    public Action<WebView2, CoreWebView2NewWindowRequestedEventArgs>? NewWindowRequestedHandler { get; set; }

    /// <summary>创建一个尚未导航的 WebView2 控件；初始化与环境绑定由 <see cref="InitializeAsync"/> 负责。</summary>
    public WebView2 CreateView()
    {
        var view = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            DefaultBackgroundColor = System.Drawing.Color.White,
        };

        view.NavigationStarting += (_, _) =>
        {
            _loadingViews.Add(view);
            if (IsActive(view))
            {
                _feedback.OnStatus("正在加载…");
                _feedback.OnProgress(true, 0);
            }
        };

        view.NavigationCompleted += (_, e) => OnNavigationCompleted(view, e);

        return view;
    }

    /// <summary>
    /// 等待控件进入可视树 → 创建/复用共享环境 → 初始化 CoreWebView2。
    /// 控件必须先挂到窗口上（可见树）才会开始初始化，因此调用方要先挂载再调用这里。
    /// </summary>
    public async Task<CoreWebView2?> InitializeAsync(WebView2 view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_disposed)
        {
            return null;
        }

        if (view.CoreWebView2 is { } already)
        {
            return already;
        }

        try
        {
            await WaitForMountedAsync(view).ConfigureAwait(true);

            var environment = await EnsureEnvironmentAsync().ConfigureAwait(true);
            if (environment is null)
            {
                return null;
            }

            await view.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            return view.CoreWebView2;
        }
        catch (Exception ex)
        {
            ReportRuntimeFailure(ex);
            return null;
        }
    }

    /// <summary>
    /// 一次性配置：设置、权限、进程失败、历史/地址回调与新窗口拦截。
    /// 对站点页和标签页都是同一套行为；回调按“是否当前活动视图”过滤，后台页不会刷状态。
    /// </summary>
    public void ConfigureCore(WebView2 view, CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(core);

        var settings = core.Settings;
        settings.IsStatusBarEnabled = false;
        settings.AreDefaultContextMenusEnabled = true;
        settings.AreBrowserAcceleratorKeysEnabled = true;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsZoomControlEnabled = true;
        // window.close 保护脚本依赖 WebMessage 回传；该设置默认开启，这里显式声明。
        settings.IsWebMessageEnabled = true;

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
            var handler = NewWindowRequestedHandler;
            if (handler is not null)
            {
                try
                {
                    handler(view, e);
                }
                catch (Exception ex)
                {
                    // 处理器自身应当兜底；这里再兜一层，避免异常穿透 WebView2 回调。
                    e.Handled = true;
                    _feedback.OnStatus($"处理新窗口请求失败：{ex.Message}");
                }

                return;
            }

            e.Handled = true;
            TryOpenInSystemBrowser(e.Uri);
        };

        core.ProcessFailed += (_, e) =>
        {
            if (!IsActive(view))
            {
                return;
            }

            _feedback.OnStatus($"页面进程异常（{e.ProcessFailedKind}），可点击「刷新」重试。");
            _feedback.OnProgress(false, 0);
        };

        core.HistoryChanged += (_, _) =>
        {
            if (IsActive(view))
            {
                _feedback.OnHistoryChanged();
            }
        };

        core.SourceChanged += (_, _) =>
        {
            if (IsActive(view))
            {
                _feedback.OnAddressChanged(core.Source);
            }
        };
    }

    /// <summary>
    /// 在目标文档创建前安装 window.close 保护：脚本不再直接关闭浏览器窗口，
    /// 而是发回应用内消息；站点页可以忽略它，临时标签页则关闭自己。
    /// 必须在首次导航、或设置 <see cref="CoreWebView2NewWindowRequestedEventArgs.NewWindow"/> 之前完成。
    /// </summary>
    public async Task InstallWindowCloseGuardAsync(CoreWebView2 core, Action onCloseRequested)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(onCloseRequested);

        // 先注册脚本，再挂消息回调：脚本要在页面自己的文档创建时执行。
        await core.AddScriptToExecuteOnDocumentCreatedAsync(CloseWindowGuardScript).ConfigureAwait(true);

        core.WebMessageReceived += (_, args) =>
        {
            string? message;
            try
            {
                message = args.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                return;
            }

            if (string.Equals(message, CloseWindowMessage, StringComparison.Ordinal))
            {
                onCloseRequested();
            }
        };

        // 兜底：页面通过其他方式触发原生关闭请求时，仍交给上层决定，
        // 避免 WebView2 自行关闭主窗口。
        core.WindowCloseRequested += (_, _) => onCloseRequested();
    }

    /// <summary>切换活动页面。站点页与标签页共用同一个指针，工具栏命令因此自动跟随当前页面。</summary>
    public void SetActiveView(WebView2? view)
    {
        if (ReferenceEquals(ActiveView, view))
        {
            return;
        }

        ActiveView = view;
        _feedback.OnHistoryChanged();
        _feedback.OnAddressChanged(view?.CoreWebView2?.Source ?? string.Empty);

        // 切到已经打开过的页面时，进度条要反映新活动页自身的加载状态，
        // 否则标签页加载中切回缓存站点会让进度条一直转。
        _feedback.OnProgress(view is not null && _loadingViews.Contains(view), 0);
    }

    /// <summary>释放控件：先脱离视觉树，再挂到不可见的退休容器上 Dispose，避免句柄仍在树中引发异常。</summary>
    public void Retire(WebView2 view)
    {
        ArgumentNullException.ThrowIfNull(view);

        _loadingViews.Remove(view);

        if (ReferenceEquals(ActiveView, view))
        {
            SetActiveView(null);
        }

        DetachFromParent(view);
        try
        {
            _retireContainer.Children.Add(view);
            view.Dispose();
        }
        catch (Exception)
        {
            // 释放失败不影响主流程，进程会随应用退出结束。
        }

        _retireContainer.Children.Remove(view);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NewWindowRequestedHandler = null;
        ActiveView = null;
        _loadingViews.Clear();
    }

    /// <summary>打开系统默认浏览器；只接受规范化后仍是 http/https 的地址。</summary>
    public static bool TryOpenInSystemBrowser(string? url)
    {
        var normalized = UrlNormalizer.Normalize(url);
        if (normalized is null)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(normalized) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            // 没有可用浏览器时静默忽略，不影响应用本身。
            return false;
        }
    }

    private bool IsActive(WebView2 view) => ReferenceEquals(view, ActiveView);

    private Task<CoreWebView2Environment?> EnsureEnvironmentAsync()
    {
        return _environmentTask ??= CreateEnvironmentAsync();
    }

    private async Task<CoreWebView2Environment?> CreateEnvironmentAsync()
    {
        try
        {
            // 所有视图共用这一个环境实例：Cookie / Profile / 登录态一致，
            // 也让 NewWindowRequested 的 NewWindow 交接满足 WebView2 的硬性要求。
            return await CoreWebView2Environment.CreateAsync(null, _userDataFolder, null).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ReportRuntimeFailure(ex);
            return null;
        }
    }

    private void OnNavigationCompleted(WebView2 view, CoreWebView2NavigationCompletedEventArgs e)
    {
        _loadingViews.Remove(view);

        if (!IsActive(view))
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
}
