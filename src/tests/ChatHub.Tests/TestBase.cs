using ChatHub.Models;
using ChatHub.Services;

namespace ChatHub.Tests;

/// <summary>单元测试基类：每个测试使用独立临时目录，绝不触碰真实的 %AppData%\ChatHub。</summary>
public abstract class TestBase
{
    protected string TempRoot { get; private set; } = string.Empty;

    [TestInitialize]
    public void CreateTempRoot()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "ChatHubTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);
    }

    [TestCleanup]
    public void RemoveTempRoot()
    {
        try
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录偶发占用，交给系统清理即可。
        }
    }

    protected string SitesFile => Path.Combine(TempRoot, "sites.txt");

    protected SiteStore CreateStore() => new(SitesFile);
}
