namespace ChatHub.Services;

/// <summary>
/// 网址规范化与校验。只做语法层面的判断（不解析 DNS），因此可离线运行且可单元测试。
/// </summary>
public static class UrlNormalizer
{
    /// <summary>名称过长会让工具栏按钮被撑爆，因此设置上限。</summary>
    public const int MaxNameLength = 40;

    /// <summary>
    /// 规范化用户输入的网址：补全 https 前缀、剔除首尾空白。
    /// 只接受 http / https，其余（file:、javascript:、data:、纯文本等）一律返回 null。
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var candidate = input.Trim();

        // 没有写 scheme 时默认按 https 处理，方便用户直接粘贴 chat.deepseek.com。
        var hadScheme = candidate.Contains("://", StringComparison.Ordinal);
        if (!hadScheme)
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        // 单段主机名（如“随便写的中文”被解析成的 https://随便写的中文）几乎总是误输入。
        // 只放行本机名与“至少含一个点”的主机名，避免把随手写的文字存成站点。
        if (!uri.Host.Contains('.', StringComparison.Ordinal) &&
            !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    /// <summary>两个网址是否指向同一地址（忽略大小写与末尾斜杠差异）。</summary>
    public static bool AreEquivalent(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);
        if (a is null || b is null)
        {
            return false;
        }

        return string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>名称留空时用域名兜底，保证工具栏按钮始终有可读文字。</summary>
    public static string FallbackName(string? url)
    {
        var normalized = Normalize(url);
        if (normalized is null)
        {
            return "未命名站点";
        }

        var host = new Uri(normalized).Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        return host;
    }

    /// <summary>按上限裁剪名称，保证工具栏布局稳定。</summary>
    public static string ClampName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length <= MaxNameLength)
        {
            return trimmed;
        }

        return trimmed[..MaxNameLength];
    }
}
