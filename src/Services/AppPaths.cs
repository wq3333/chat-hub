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

    /// <summary>应用内更新目录：%LocalAppData%\ChatHub\update。</summary>
    public static string UpdateFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatHub", "update");

    /// <summary>下载完成的更新包；upgrade.bat 默认也读这个位置。</summary>
    public static string UpdateZipFile => Path.Combine(UpdateFolder, "win-x64.zip");

    /// <summary>升级脚本的日志文件；升级失败时可从这里排查。</summary>
    public static string UpgradeLogFile => Path.Combine(UpdateFolder, "upgrade.log");

    /// <summary>主窗口的位置/大小/最大化状态；机器本地状态，不随漫游配置走。</summary>
    public static string WindowPlacementFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatHub", "window.json");
}
