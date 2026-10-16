using System.IO;

namespace ChatHub.Services;

/// <summary>
/// 应用使用的所有磁盘路径集中在此，便于查阅与测试覆盖。
/// </summary>
public static class AppPaths
{
    /// <summary>应用数据根目录：%AppData%\ChatHub（可漫游）。</summary>
    public static string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChatHub");

    /// <summary>站点清单位置：%AppData%\ChatHub\sites.txt。</summary>
    public static string SitesFile => Path.Combine(DataRoot, "sites.txt");

    /// <summary>WebView2 用户数据目录：%LocalAppData%\ChatHub\WebView2（浏览器缓存属机器本地数据，不漫游）。</summary>
    public static string WebView2UserDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatHub", "WebView2");
}
