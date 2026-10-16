using System.Collections.ObjectModel;
using ChatHub.Models;
using ChatHub.Services;
using ChatHub.ViewModels;

namespace ChatHub.Tests;

[TestClass]
public sealed class SiteManagerViewModelTests : TestBase
{
    private readonly List<string> _errors = [];
    private SiteStore _store = null!;
    private ObservableCollection<Site> _sites = null!;
    private SiteManagerViewModel _manager = null!;
    private int _seedCount;

    [TestInitialize]
    public void SetUp()
    {
        _store = CreateStore();
        _sites = new ObservableCollection<Site>(_store.Load());
        _seedCount = _sites.Count; // 默认种子站点数量由应用定义，测试不写死
        _manager = new SiteManagerViewModel(_store, _sites)
        {
            ConfirmDelete = _ => true,
            ShowError = _errors.Add,
        };
    }

    [TestMethod]
    public void Add_WritesToFileAndCollection()
    {
        _manager.ShowEditDialog = editor =>
        {
            editor.Name = "Kimi";
            editor.Url = "kimi.moonshot.cn";
            return true;
        };

        _manager.Add();

        Assert.HasCount(_seedCount + 1, _sites);
        Assert.AreEqual("Kimi", _sites[^1].Name);
        Assert.AreEqual("https://kimi.moonshot.cn/", _sites[^1].Url);

        var reloaded = CreateStore().Load();
        Assert.HasCount(_seedCount + 1, reloaded);
        Assert.AreEqual("https://kimi.moonshot.cn/", reloaded[^1].Url);
        Assert.AreEqual("Kimi", reloaded[^1].Name);
    }

    [TestMethod]
    public void Add_CancelledDialogChangesNothing()
    {
        _manager.ShowEditDialog = _ => false;

        _manager.Add();

        Assert.HasCount(_seedCount, _sites);
        Assert.HasCount(_seedCount, CreateStore().Load());
    }

    [TestMethod]
    public void Edit_UpdatesSiteAndFile()
    {
        var target = _sites[0];
        _manager.OnSelectionChanged(target);
        _manager.ShowEditDialog = editor =>
        {
            editor.Name = "豆包新版";
            editor.Url = "https://www.doubao.com/chat/?from=chathub";
            return true;
        };

        _manager.Edit();

        Assert.AreEqual("豆包新版", target.Name);
        var reloaded = CreateStore().Load();
        Assert.AreEqual("豆包新版", reloaded[0].Name);
        Assert.AreEqual("https://www.doubao.com/chat/?from=chathub", reloaded[0].Url);
    }

    [TestMethod]
    public void Delete_RemovesSiteFromCollectionAndFile()
    {
        var target = _sites[0];
        var expectedRemaining = _sites[0].Name == "豆包" ? "DeepSeek" : _sites[1].Name;

        _manager.OnSelectionChanged(target);
        _manager.Delete();

        Assert.HasCount(_seedCount - 1, _sites);
        Assert.IsFalse(_sites.Contains(target), "被删除的站点必须从集合里移除");

        var reloaded = CreateStore().Load();
        Assert.HasCount(_seedCount - 1, reloaded);
        Assert.AreEqual(expectedRemaining, reloaded[0].Name);
    }

    [TestMethod]
    public void Delete_DeclinedKeepsEverything()
    {
        _manager.ConfirmDelete = _ => false;
        _manager.OnSelectionChanged(_sites[0]);

        _manager.Delete();

        Assert.HasCount(_seedCount, _sites);
    }

    [TestMethod]
    public void Filter_MatchesNameOrUrl()
    {
        _manager.FilterText = "deep";
        var matched = _manager.SitesView.Cast<Site>().ToList();
        Assert.HasCount(1, matched);
        Assert.AreEqual("DeepSeek", matched[0].Name);

        _manager.FilterText = "doubao.com";
        Assert.HasCount(1, _manager.SitesView.Cast<Site>());

        _manager.FilterText = "不存在的关键字";
        Assert.IsEmpty(_manager.SitesView.Cast<Site>().ToList());

        _manager.FilterText = string.Empty;
        Assert.HasCount(_seedCount, _manager.SitesView.Cast<Site>());
    }

    [TestMethod]
    public void Add_RollsBack_WhenFileCannotBeWritten()
    {
        var failing = new FailingStore(SitesFile);
        var sites = new ObservableCollection<Site>(_store.Load());
        var manager = new SiteManagerViewModel(failing, sites) { ShowError = _errors.Add };
        manager.ShowEditDialog = editor =>
        {
            editor.Url = "kimi.moonshot.cn";
            return true;
        };

        manager.Add();

        Assert.HasCount(_seedCount, sites, "保存失败时必须回滚内存改动");
        Assert.HasCount(1, _errors);
        Assert.HasCount(_seedCount, CreateStore().Load(), "原文件内容不应被破坏");
    }

    /// <summary>模拟磁盘写入失败（只读目录、文件被占用等）。</summary>
    private sealed class FailingStore : SiteStore
    {
        public FailingStore(string filePath)
            : base(filePath)
        {
        }

        public override void Save(IEnumerable<Site> sites) =>
            throw new IOException("磁盘不可写（测试模拟）");
    }
}
