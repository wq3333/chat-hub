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
///
/// 临时标签页不属于这个池：它们由 <see cref="InAppTabHost"/> 单独管理、关闭即释放。
/// 两者只共享 <see cref="WebViewRuntime"/> 里的环境与活动视图指针。
/// </summary>
public sealed class WebViewPool : IDisposable
{
    /// <summary>WebView2 运行时缺失时的官方下载入口（Evergreen Bootstrapper）。</summary>
    public const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private readonly Dictionary<string, WebView2> _views = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>每站点的 CoreWebView2 就绪任务：控件挂到窗口上之后才真正开始初始化。</summary>
    private readonly Dictionary<string, Task<CoreWebView2?>> _readyCores = new(StringComparer.OrdinalIgnoreCase);

    private readonly WebViewRuntime _runtime;
    private bool _disposed;

    public WebViewPool(WebViewRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>当前真正显示在内容区的页面（可能是站点页，也可能是临时标签页）。</summary>
    public WebView2? CurrentView => _runtime.ActiveView;

    public bool HasActiveView => _runtime.HasActiveView;

    public bool CanGoBack => _runtime.ActiveCore?.CanGoBack ?? false;

    public bool CanGoForward => _runtime.ActiveCore?.CanGoForward ?? false;

    public void SetCurrent(WebView2? view) => _runtime.SetActiveView(view);

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

        var view = _runtime.CreateView();
        _views[site.Id] = view;
        return view;
    }

    /// <summary>某站点是否已经创建过页面。</summary>
    public bool IsCached(string siteId) => _views.ContainsKey(siteId);

    /// <summary>
    /// 确保页面已就绪并处于该站点的网址上。
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
        if (_runtime.ActiveCore is { CanGoBack: true } core)
        {
            core.GoBack();
        }
    }

    public void GoForward()
    {
        if (_runtime.ActiveCore is { CanGoForward: true } core)
        {
            core.GoForward();
        }
    }

    public void Reload() => _runtime.ActiveCore?.Reload();

    /// <summary>导航到指定网址（界面层已校验，这里仍做一次兜底）。</summary>
    public void Navigate(string url)
    {
        if (_runtime.ActiveCore is { } core)
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

        _runtime.Retire(view);
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
        _readyCores.Clear();
        _runtime.SetActiveView(null);
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
        var core = await _runtime.InitializeAsync(view).ConfigureAwait(true);
        if (core is null)
        {
            _readyCores.Remove(site.Id);
            _runtime.Feedback.OnStatus($"「{site.Name}」的页面组件初始化失败，可点击「刷新」重试。");
            return null;
        }

        _runtime.ConfigureCore(view, core);

        try
        {
            // 站点页不是独立弹窗：页面调用 window.close() 时忽略，避免 WebView2 连带关闭主窗口。
            await _runtime.InstallWindowCloseGuardAsync(core, OnSiteCloseRequested).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _runtime.Feedback.OnStatus($"站点页关闭保护初始化失败：{ex.Message}");
        }

        return core;
    }

    private void OnSiteCloseRequested()
    {
        _runtime.Feedback.OnStatus("页面请求关闭窗口，已忽略。");
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

    private static void NavigateCore(CoreWebView2 core, string url)
    {
        var normalized = UrlNormalizer.Normalize(url);
        if (normalized is not null)
        {
            core.Navigate(normalized);
        }
    }
}
