using System.Text;
using ChatHub.Models;
using ChatHub.Services;

namespace ChatHub.Tests;

[TestClass]
public sealed class SiteStoreTests : TestBase
{
    /// <summary>默认种子站点的数量由应用定义，测试不写死具体个数。</summary>
    private static int SeedCount => SiteStore.CreateDefaults().Count;

    [TestMethod]
    public void Load_SeedsDefaultsOnFirstRun_AndWritesFile()
    {
        var store = CreateStore();

        var sites = store.Load(out var skipped);

        Assert.AreEqual(0, skipped);
        Assert.HasCount(SeedCount, sites);
        CollectionAssert.AreEqual(
            SiteStore.CreateDefaults().Select(s => s.Name).ToArray(),
            sites.Select(s => s.Name).ToArray(),
            "首次运行应按定义顺序写入全部默认站点");
        CollectionAssert.AreEqual(
            SiteStore.CreateDefaults().Select(s => s.Url).ToArray(),
            sites.Select(s => s.Url).ToArray(),
            "首次运行写入的网址应与默认站点一致");
        Assert.IsTrue(File.Exists(SitesFile), "首次运行应把默认站点写入 sites.txt");
    }

    [TestMethod]
    public void SeedDefaults_AreValidUniqueSites()
    {
        var defaults = SiteStore.CreateDefaults();

        Assert.IsNotEmpty(defaults, "至少要有一个默认站点，否则首次运行是空白界面");
        Assert.AreEqual(
            defaults.Count,
            defaults.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "默认站点 id 不能重复");
        CollectionAssert.AreEqual(
            defaults.Select(s => s.Url).ToArray(),
            defaults.Select(s => UrlNormalizer.Normalize(s.Url)).ToArray(),
            "默认站点的网址必须都是合法的 http/https 地址");
        Assert.IsTrue(defaults.All(s => s.Name.Trim().Length > 0), "默认站点必须有名称");
    }

    [TestMethod]
    public void Load_TreatsWhitespaceOnlyFileAsFirstRun()
    {
        File.WriteAllText(SitesFile, "   " + Environment.NewLine, Encoding.UTF8);

        var sites = CreateStore().Load(out _);

        Assert.HasCount(SeedCount, sites, "空文件应当按首次运行处理并写入默认站点");
    }

    [TestMethod]
    public void Load_RespectsUserClearedList_WithoutReseeding()
    {
        File.WriteAllText(
            SitesFile,
            "# ChatHub 站点清单" + Environment.NewLine + "# id\tname\turl" + Environment.NewLine,
            Encoding.UTF8);

        var sites = CreateStore().Load(out var skipped);

        Assert.IsEmpty(sites, "用户清空后不应重新写入默认站点");
        Assert.AreEqual(0, skipped);
    }

    [TestMethod]
    public void Load_ParsesValidLines_AndSkipsBrokenOnes()
    {
        File.WriteAllLines(
            SitesFile,
            [
                "# 注释行应被忽略",
                string.Empty,
                "# id\tname\turl",
                "aaaa1111\t豆包\thttps://www.doubao.com/chat/",
                "bbbb2222\tDeepSeek\tchat.deepseek.com",
                "cccc3333\t坏网址\thttps://bad url.example.com/",
                "dddd4444\t只有一个字段",
            ],
            Encoding.UTF8);

        var sites = CreateStore().Load(out var skipped);

        Assert.AreEqual(2, skipped, "含空格的网址行与只有一列的行都应被跳过");
        Assert.HasCount(2, sites);
        Assert.AreEqual("豆包", sites[0].Name);
        Assert.AreEqual("https://www.doubao.com/chat/", sites[0].Url);
        Assert.AreEqual("DeepSeek", sites[1].Name);
        Assert.AreEqual("https://chat.deepseek.com/", sites[1].Url, "缺少 scheme 的网址应补上 https");
    }

    [TestMethod]
    public void Load_ReassignsDuplicateIds()
    {
        File.WriteAllLines(
            SitesFile,
            [
                "sameid01\t甲\thttps://a.example.com/",
                "sameid01\t乙\thttps://b.example.com/",
            ],
            Encoding.UTF8);

        var sites = CreateStore().Load(out var skipped);

        Assert.AreEqual(0, skipped);
        Assert.HasCount(2, sites);
        Assert.AreNotEqual(sites[0].Id, sites[1].Id, "重复 id 必须被重新分配，否则 WebView 池会串页");
        Assert.AreEqual("https://b.example.com/", sites[1].Url);
    }

    [TestMethod]
    public void Load_FillsMissingId_AndFallsBackToHostForBlankName()
    {
        File.WriteAllLines(
            SitesFile,
            [
                "https://kimi.moonshot.cn/",
                "\t\tchat.deepseek.com",
            ],
            Encoding.UTF8);

        var sites = CreateStore().Load(out var skipped);

        Assert.AreEqual(0, skipped, "整行只有一个网址时应当自动补名称与 id");
        Assert.HasCount(2, sites);
        Assert.IsTrue(sites.All(s => s.Id.Length > 0), "缺少 id 的行必须自动补 id");
        Assert.AreEqual("https://kimi.moonshot.cn/", sites[0].Url);
        Assert.AreEqual("kimi.moonshot.cn", sites[0].Name, "名称为空时用域名兜底");
        Assert.AreEqual("chat.deepseek.com", sites[1].Name);
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripsInOrder()
    {
        var store = CreateStore();
        var original = new List<Site>
        {
            new("id000001", "甲", "https://a.example.com/"),
            new("id000002", "乙", "https://b.example.com/chat/"),
            new("id000003", "丙", "https://c.example.com/"),
        };

        store.Save(original);
        var reloaded = CreateStore().Load(out var skipped);

        Assert.AreEqual(0, skipped);
        CollectionAssert.AreEqual(
            original.Select(s => s.Id).ToArray(),
            reloaded.Select(s => s.Id).ToArray());
        CollectionAssert.AreEqual(
            original.Select(s => s.Name).ToArray(),
            reloaded.Select(s => s.Name).ToArray());
        CollectionAssert.AreEqual(
            original.Select(s => s.Url).ToArray(),
            reloaded.Select(s => s.Url).ToArray());
    }

    [TestMethod]
    public void Save_WritesUtf8WithBom_AndSanitizesTabs()
    {
        var store = CreateStore();
        store.Save([new Site("tab00001", "含\t制表符", "https://a.example.com/")]);

        var bytes = File.ReadAllBytes(SitesFile);
        Assert.AreEqual(0xEF, bytes[0]);
        Assert.AreEqual(0xBB, bytes[1]);
        Assert.AreEqual(0xBF, bytes[2], "写入应为带 BOM 的 UTF-8，方便记事本识别");

        var reloaded = store.Load(out _);
        Assert.HasCount(1, reloaded);
        Assert.AreEqual("含 制表符", reloaded[0].Name);
    }

    [TestMethod]
    public void Save_LeavesNoTempFileBehind()
    {
        var store = CreateStore();
        store.Save([new Site("tmp00001", "甲", "https://a.example.com/")]);

        Assert.IsFalse(File.Exists(SitesFile + ".tmp"), "原子写入完成后不应残留 .tmp 文件");
    }
}
