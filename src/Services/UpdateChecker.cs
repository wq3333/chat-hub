using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ChatHub.Services;

/// <summary>
/// 更新检查的纯逻辑：解析 upgrade.bat check 的 JSON，并比较版本号。
/// 不进行任何 IO，便于单元测试覆盖。
/// </summary>
public static partial class UpdateChecker
{
    /// <summary>允许前导 v/V，其余原样返回；不做大小写或分隔符归一化。</summary>
    public static string NormalizeTag(string? tag)
    {
        var value = (tag ?? string.Empty).Trim();
        while (value.Length > 0 && (value[0] == 'v' || value[0] == 'V'))
        {
            value = value[1..].Trim();
        }

        return value;
    }

    /// <summary>
    /// 解析形如 1、1.2、1.2.3、1.2.3.4 的版本主体；预发布后缀（-beta）不参与比较。
    /// 统一补足到 4 段，避免 "1.0" 与 "1.0.0" 被 System.Version 判成不同。
    /// </summary>
    public static bool TryParseNumericVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);

        var match = NumericVersionPattern().Match(NormalizeTag(value));
        if (!match.Success)
        {
            return false;
        }

        var parts = match.Groups[1].Value.Split('.');
        var numbers = new int[4];
        for (var i = 0; i < parts.Length && i < numbers.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    /// <summary>latest 是否严格大于 current；任一版本无法解析时返回 false（保守地不提示更新）。</summary>
    public static bool IsNewer(string? latestVersion, string? currentVersion)
    {
        if (!TryParseNumericVersion(latestVersion, out var latest) ||
            !TryParseNumericVersion(currentVersion, out var current))
        {
            return false;
        }

        return latest > current;
    }

    /// <summary>按数值比较两个版本：正数表示 latest 更新，0 表示相同，负数表示更旧，null 表示无法比较。</summary>
    public static int? Compare(string? latestVersion, string? currentVersion)
    {
        if (!TryParseNumericVersion(latestVersion, out var latest) ||
            !TryParseNumericVersion(currentVersion, out var current))
        {
            return null;
        }

        return latest.CompareTo(current);
    }

    /// <summary>
    /// 解析 check 输出的单行 JSON。成功对象包含 version/tag_name/.../download_url；
    /// 失败对象为 {"error":"..."}。任何结构问题都转换成可展示的错误，不抛异常。
    /// </summary>
    public static UpdateCheckResult ParseCheckResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return UpdateCheckResult.Failed("更新检查没有返回任何内容。");
        }

        CheckResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CheckResponseDto>(json.Trim().TrimStart('\uFEFF'));
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failed("无法解析更新检查结果，请重试或检查网络。");
        }

        if (dto is null)
        {
            return UpdateCheckResult.Failed("更新检查返回了空结果。");
        }

        if (!string.IsNullOrWhiteSpace(dto.Error))
        {
            return UpdateCheckResult.Failed(dto.Error.Trim());
        }

        var version = string.IsNullOrWhiteSpace(dto.Version)
            ? NormalizeTag(dto.TagName)
            : dto.Version.Trim();

        if (string.IsNullOrWhiteSpace(version))
        {
            return UpdateCheckResult.Failed("更新信息缺少版本号。");
        }

        if (string.IsNullOrWhiteSpace(dto.DownloadUrl) ||
            !Uri.TryCreate(dto.DownloadUrl.Trim(), UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            return UpdateCheckResult.Failed("更新信息缺少有效的 https 下载地址。");
        }

        DateTimeOffset? publishedAt = null;
        if (DateTimeOffset.TryParse(dto.PublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            publishedAt = parsed;
        }

        var tagName = string.IsNullOrWhiteSpace(dto.TagName) ? "v" + version : dto.TagName.Trim();

        return UpdateCheckResult.Ok(new ReleaseInfo
        {
            Version = version,
            TagName = tagName,
            Name = dto.Name?.Trim() ?? string.Empty,
            Notes = dto.Body?.Trim() ?? string.Empty,
            PageUrl = string.IsNullOrWhiteSpace(dto.HtmlUrl) ? AppInfo.ReleasesUrl : dto.HtmlUrl.Trim(),
            DownloadUrl = downloadUri.AbsoluteUri,
            PublishedAt = publishedAt,
            AssetSize = dto.Size is > 0 ? dto.Size : null,
        });
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+){0,3})")]
    private static partial Regex NumericVersionPattern();

    private sealed class CheckResponseDto
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
