using System.Collections.ObjectModel;
using ChatHub.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatHub.Services;

/// <summary>
/// 临时标签页宿主：只负责标签页的创建、初始化、释放与页面事件，
/// 不接管“当前选中哪个标签”的状态（选中/切换由 <see cref="ChatHub.ViewModels.MainWindowViewModel"/> 协调）。
///
/// 每个标签页独占一个 WebView2，关闭即 Dispose；不按站点 id 缓存，也不进入 <see cref="WebViewPool"/>。
/// 它和站点页共用 <see cref="WebViewRuntime"/> 的环境，因此 Cookie / 登录态一致，
/// 且可以通过 <see cref="CoreWebView2NewWindowRequestedEventArgs.NewWindow"/> 保留 window.opener。
/// </summary>
public sealed class InAppTabHost : IDisposable
{
    private readonly WebViewRuntime _runtime;
    private readonly IWebViewHostFeedback _feedback;
    private readonly Dictionary<InAppTab, WebView2> _views = new();

    private bool _disposed;

    public InAppTabHost(WebViewRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _feedback = runtime.Feedback;
    }

    public ObservableCollection<InAppTab> Tabs { get; } = [];

    /// <summary>新标签已经创建并加入集合，调用方应立刻把它挂到内容区，再继续异步初始化。</summary>
    public event Action<InAppTab>? TabCreated;

    /// <summary>页面自身调用 window.close()，由界面层按关闭标签的规则处理。</summary>
    public event Action<InAppTab>? CloseRequested;

    /// <summary>标签页创建/初始化失败，标签已从集合移除，调用方需要回退到之前的页面。</summary>
    public event Action<InAppTab>? TabOpenFailed;

    public WebView2? GetView(InAppTab tab) => _views.GetValueOrDefault(tab);

    /// <summary>移除并释放一个标签页；不改变当前活动视图，由调用方先处理切换。</summary>
    public void Remove(InAppTab tab)
    {
        if (!_views.Remove(tab, out var view))
        {
            Tabs.Remove(tab);
            return;
        }

        Tabs.Remove(tab);
        _runtime.Retire(view);
    }

    /// <summary>
    /// 处理任意站点页/标签页发出的新窗口请求。
    /// 成功时把新建标签页的 CoreWebView2 交给 WebView2 作为 NewWindow，保留 opener 关系；
    /// 失败时标记 Handled 并清理标签页，避免 WebView2 再自开一个不受控的弹窗。
    /// </summary>
    public async void HandleNewWindowRequested(WebView2 opener, CoreWebView2NewWindowRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_disposed)
        {
            e.Handled = true;
            return;
        }

        if (!NewWindowPolicy.CanOpenNewTab(Tabs.Count))
        {
            e.Handled = true;
            _feedback.OnStatus($"临时标签页最多同时打开 {NewWindowPolicy.MaxTabs} 个，已拦截新的弹窗。");
            return;
        }

        if (!NewWindowPolicy.CanOpenInApp(e.Uri))
        {
            e.Handled = true;
            _feedback.OnStatus($"已拦截非 http(s) 的新窗口请求：{e.Uri}");
            return;
        }

        InAppTab? tab = null;
        WebView2? view = null;
        var newWindowAssigned = false;

        try
        {
            view = _runtime.CreateView();
            tab = new InAppTab(NewWindowPolicy.InitialTitle(e.Uri), NormalizeTabUrl(e.Uri));
            _views[tab] = view;
            Tabs.Add(tab);

            // 先把视图挂进内容区：WebView2 只有在进入可视树之后才能初始化。
            // 之后才等待初始化，不能在这里先 Navigate，否则会破坏 NewWindow 的交接。
            TabCreated?.Invoke(tab);

            using (var deferral = e.GetDeferral())
            {
                var core = await _runtime.InitializeAsync(view).ConfigureAwait(true);
                if (_disposed || !Tabs.Contains(tab) || !_views.TryGetValue(tab, out var currentView) ||
                    !ReferenceEquals(currentView, view))
                {
                    // 用户在初始化期间关掉了这个标签页（或应用正在退出）。
                    e.Handled = true;
                    return;
                }

                if (core is null)
                {
                    Fail(tab, e, "无法初始化新标签页的渲染组件。");
                    return;
                }

                // 设置与事件必须在设置 NewWindow 之前完成，才能作用到新页面上。
                // 其中关闭保护脚本是 window.close() 不关闭主窗口、而是关闭本标签页的关键。
                await _runtime.InstallWindowCloseGuardAsync(core, () => RequestClose(tab, view)).ConfigureAwait(true);
                _runtime.ConfigureCore(view, core);
                AttachTabEvents(tab, core);

                e.NewWindow = core;
                newWindowAssigned = true;
                _feedback.OnStatus($"已在新标签页打开：{tab.Title}");
            }
        }
        catch (Exception ex)
        {
            if (!newWindowAssigned)
            {
                e.Handled = true;
            }

            if (tab is not null)
            {
                Fail(tab, e, $"打开新标签页失败：{ex.Message}", openExternal: !newWindowAssigned);
            }
            else
            {
                _feedback.OnStatus($"打开新标签页失败：{ex.Message}");
            }
        }
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
            _runtime.Retire(view);
        }

        _views.Clear();
        Tabs.Clear();
    }

    private void AttachTabEvents(InAppTab tab, CoreWebView2 core)
    {
        core.DocumentTitleChanged += (_, _) => UpdateTitle(tab, core);
        core.SourceChanged += (_, _) =>
        {
            tab.Url = NormalizeTabUrl(core.Source);
            UpdateTitle(tab, core);
        };
    }

    private void RequestClose(InAppTab tab, WebView2 view)
    {
        // 不在控件自己的事件回调里同步 Dispose，延后到下一轮 Dispatcher。
        view.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed)
            {
                CloseRequested?.Invoke(tab);
            }
        }));
    }

    private static void UpdateTitle(InAppTab tab, CoreWebView2 core)
    {
        var documentTitle = core.DocumentTitle;
        if (!string.IsNullOrWhiteSpace(documentTitle))
        {
            tab.Title = documentTitle.Trim();
            return;
        }

        tab.Title = string.IsNullOrWhiteSpace(tab.Url)
            ? "新标签页"
            : UrlNormalizer.FallbackName(tab.Url);
    }

    /// <summary>把 WebView2 的地址转成标签展示用地址；about:blank 等非 http(s) 地址显示为空。</summary>
    private static string NormalizeTabUrl(string? uri) => UrlNormalizer.Normalize(uri) ?? string.Empty;

    private void Fail(
        InAppTab tab,
        CoreWebView2NewWindowRequestedEventArgs e,
        string message,
        bool openExternal = true)
    {
        e.Handled = true;

        Remove(tab);
        TabOpenFailed?.Invoke(tab);
        _feedback.OnStatus(message);

        // 初始化失败时至少别让用户丢掉链接；正常拦截路径不会走到这里。
        if (openExternal && NewWindowPolicy.CanOpenInApp(e.Uri) && !IsBlankPage(e.Uri))
        {
            WebViewRuntime.TryOpenInSystemBrowser(e.Uri);
        }
    }

    private static bool IsBlankPage(string? uri) =>
        string.IsNullOrWhiteSpace(uri) ||
        uri.Trim().Equals("about:blank", StringComparison.OrdinalIgnoreCase);
}
