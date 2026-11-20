namespace ChatHub.Services;

/// <summary>GitHub 最新 release 的展示/下载信息，由 <c>upgrade.bat check</c> 输出的 JSON 构造。</summary>
public sealed record ReleaseInfo
{
    /// <summary>去掉前导 v 的版本号，例如 1.0.1。</summary>
    public required string Version { get; init; }

    /// <summary>release 的 tag，例如 v1.0.1。</summary>
    public required string TagName { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>release 正文（当前由 CI 写入触发提交的完整 commit message）。</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>release 页面地址。</summary>
    public string PageUrl { get; init; } = string.Empty;

    /// <summary>win-x64.zip 的直链。</summary>
    public required string DownloadUrl { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>资产大小（字节）；缺失时下载进度按未知长度处理。</summary>
    public long? AssetSize { get; init; }
}

/// <summary>check 的结果：要么带回 release，要么带回可展示的错误文本。</summary>
public sealed class UpdateCheckResult
{
    private UpdateCheckResult(ReleaseInfo? release, string? error)
    {
        Release = release;
        Error = error;
    }

    public ReleaseInfo? Release { get; }

    public string? Error { get; }

    public bool Success => Release is not null && string.IsNullOrEmpty(Error);

    public static UpdateCheckResult Ok(ReleaseInfo release) =>
        new(release ?? throw new ArgumentNullException(nameof(release)), null);

    public static UpdateCheckResult Failed(string error) =>
        new(null, string.IsNullOrWhiteSpace(error) ? "无法获取更新信息。" : error);
}

/// <summary>下载进度快照；TotalBytes 未知时 <see cref="Percent"/> 为 null。</summary>
public readonly record struct DownloadProgress(long ReceivedBytes, long? TotalBytes)
{
    public double? Percent =>
        TotalBytes is > 0
            ? Math.Clamp(ReceivedBytes * 100.0 / TotalBytes.Value, 0, 100)
            : null;
}
