using System.Reflection;

namespace ChatHub.Services;

/// <summary>
/// 应用身份与版本信息：关于窗口、检查更新与 GitHub workflow 需要保持同一套仓库/版本口径。
/// </summary>
public static class AppInfo
{
    public const string ProductName = "ChatHub";

    /// <summary>GitHub 仓库标识，与 <c>src/upgrade.bat</c> 中的 $repo 保持一致。</summary>
    public const string RepositorySlug = "wq3333/chat-hub";

    public const string RepositoryUrl = "https://github.com/" + RepositorySlug;

    public const string ReleasesUrl = RepositoryUrl + "/releases";

    /// <summary>
    /// 当前版本，来自 csproj 的 &lt;Version&gt;（编译进 AssemblyInformationalVersion）。
    /// SourceLink 等工具可能追加 <c>+&lt;commit&gt;</c> 元数据，这里只保留版本主体。
    /// </summary>
    public static string CurrentVersion
    {
        get
        {
            var assembly = typeof(AppInfo).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var raw = string.IsNullOrWhiteSpace(informational)
                ? assembly.GetName().Version?.ToString()
                : informational;
            return NormalizeDisplayVersion(raw);
        }
    }

    /// <summary>把 "1.0.0+abc123" 之类的值裁剪成用于展示/比较的版本主体。</summary>
    public static string NormalizeDisplayVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "0.0.0";
        }

        var value = raw.Trim();
        var metadataIndex = value.IndexOf('+');
        if (metadataIndex >= 0)
        {
            value = value[..metadataIndex].Trim();
        }

        return value.Length == 0 ? "0.0.0" : value;
    }
}
