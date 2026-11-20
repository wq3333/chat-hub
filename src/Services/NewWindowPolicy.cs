namespace ChatHub.Services;

/// <summary>
/// 新窗口请求的路由策略：哪些链接适合放进应用内标签页、最多允许几个标签页。
/// 不涉及 WebView2 或 UI，因此可以单独单元测试。
/// </summary>
public static class NewWindowPolicy
{
    /// <summary>同时打开的临时标签页上限，防止弹窗脚本或异常页面无限开页。</summary>
    public const int MaxTabs = 12;

    /// <summary>
    /// 是否允许在应用内标签页打开。空地址与 about:blank 允许（脚本先开窗再写入内容）；
    /// 其余只接受 http / https。mailto、file、javascript、自定义协议一律拦截。
    /// </summary>
    public static bool CanOpenInApp(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return true;
        }

        var candidate = uri.Trim();
        if (candidate.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)
               && parsed.Scheme is "http" or "https"
               && !string.IsNullOrWhiteSpace(parsed.Host);
    }

    /// <summary>当前已打开标签数是否还允许再开一个。</summary>
    public static bool CanOpenNewTab(int openTabs) => openTabs >= 0 && openTabs < MaxTabs;

    /// <summary>标签页初始标题：http(s) 用域名兜底，空白页或无效地址显示「新标签页」。</summary>
    public static string InitialTitle(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) ||
            uri.Trim().Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return "新标签页";
        }

        // 新窗口请求的 Uri 通常是绝对地址；这里仍允许省略 scheme，方便标题兜底与测试。
        // 已带非 http(s) scheme 的地址直接显示占位标题，避免 UrlNormalizer 给缺失 scheme 的文本补前缀后误判。
        if (Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var parsed) &&
            parsed.Scheme != Uri.UriSchemeHttp &&
            parsed.Scheme != Uri.UriSchemeHttps)
        {
            return "新标签页";
        }

        var normalized = UrlNormalizer.Normalize(uri);
        return normalized is null ? "新标签页" : UrlNormalizer.FallbackName(normalized);
    }
}
