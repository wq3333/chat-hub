using ChatHub.ViewModels;

namespace ChatHub.Models;

/// <summary>
/// 从站点页面新窗口请求中产生的临时标签页。
/// 只保存展示数据；真正的 WebView2 由 <see cref="ChatHub.Services.InAppTabHost"/> 单独持有并释放，
/// 不进入按站点缓存的页面池。
/// </summary>
public sealed class InAppTab : ObservableObject
{
    private string _title = "新标签页";
    private string _url = string.Empty;
    private bool _isActive;

    public InAppTab(string? title = null, string? url = null)
    {
        Id = Guid.NewGuid().ToString("N")[..8];
        _title = string.IsNullOrWhiteSpace(title) ? "新标签页" : title;
        _url = url ?? string.Empty;
    }

    /// <summary>本次运行内的短标识，仅用于调试与区分实例。</summary>
    public string Id { get; }

    /// <summary>标签条显示的标题；页面标题为空时由调用方回退为域名。</summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, string.IsNullOrWhiteSpace(value) ? "新标签页" : value);
    }

    /// <summary>当前地址（空字符串表示 about:blank / 尚未导航）。</summary>
    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value ?? string.Empty);
    }

    /// <summary>是否为当前显示的标签页，仅用于标签条高亮。</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public override string ToString() => $"{Title} <{Url}>";
}
