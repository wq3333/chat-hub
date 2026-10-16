using ChatHub.Models;
using ChatHub.Services;
using ChatHub.ViewModels;

namespace ChatHub.Tests;

[TestClass]
public sealed class SiteEditViewModelTests
{
    private static readonly List<Site> Existing =
    [
        new("exist001", "豆包", "https://www.doubao.com/chat/"),
    ];

    [TestMethod]
    public void ForNew_NormalizesBareDomain()
    {
        var editor = SiteEditViewModel.ForNew(Existing, presetName: "Kimi", presetUrl: "kimi.moonshot.cn");

        Assert.AreEqual("https://kimi.moonshot.cn/", editor.NormalizedUrl);
        Assert.IsFalse(editor.HasValidationMessage);
        Assert.IsTrue(editor.OkCommand.CanExecute(null), "合法网址应当允许保存");
    }

    [TestMethod]
    public void ForNew_BlocksNonHttpSchemes()
    {
        var editor = SiteEditViewModel.ForNew(Existing, presetUrl: "javascript:alert(1)");

        Assert.IsTrue(editor.HasValidationMessage);
        Assert.IsFalse(editor.OkCommand.CanExecute(null), "非法网址必须禁止保存");
        Assert.AreEqual(string.Empty, editor.NormalizedUrl);
    }

    [TestMethod]
    public void EmptyUrl_ReportsValidationMessage()
    {
        var editor = SiteEditViewModel.ForNew(Existing);

        Assert.IsTrue(editor.HasValidationMessage, "空网址必须给出提示，否则会写坏站点清单");
        Assert.IsFalse(editor.OkCommand.CanExecute(null));
    }

    [TestMethod]
    public void DuplicateUrl_IsRejected_ButSameSiteIsAllowed()
    {
        var duplicate = SiteEditViewModel.ForNew(Existing, presetUrl: "https://www.doubao.com/chat/");
        Assert.IsTrue(duplicate.HasValidationMessage);
        Assert.IsFalse(duplicate.OkCommand.CanExecute(null));

        // 修改既有站点时不应把自己判为重复。
        var self = SiteEditViewModel.ForEdit(Existing[0], Existing);
        Assert.IsFalse(self.HasValidationMessage);
        Assert.IsTrue(self.OkCommand.CanExecute(null));
    }

    [TestMethod]
    public void OkCommand_ReactsToInputChanges()
    {
        var editor = SiteEditViewModel.ForNew(Existing);
        Assert.IsTrue(editor.HasValidationMessage);
        Assert.IsFalse(editor.OkCommand.CanExecute(null));

        editor.Url = "example.com";
        Assert.IsFalse(editor.HasValidationMessage);
        Assert.IsTrue(editor.OkCommand.CanExecute(null));

        editor.Url = "javascript:alert(1)";
        Assert.IsFalse(editor.OkCommand.CanExecute(null), "改回非法值后必须重新禁用保存");
        Assert.AreEqual(string.Empty, editor.NormalizedUrl, "非法输入不能残留旧的规范化结果");
    }

    [TestMethod]
    public void ResolveFinalName_FallsBackToHost_AndClampsLength()
    {
        var blank = SiteEditViewModel.ForNew(Existing, presetUrl: "https://www.doubao.com/chat/");
        Assert.AreEqual("doubao.com", blank.ResolveFinalName());

        var tooLong = SiteEditViewModel.ForNew(Existing, presetName: new string('长', 80), presetUrl: "example.com");
        Assert.AreEqual(UrlNormalizer.MaxNameLength, tooLong.ResolveFinalName().Length);

        var named = SiteEditViewModel.ForNew(Existing, presetName: "  我的助手  ", presetUrl: "example.com");
        Assert.AreEqual("我的助手", named.ResolveFinalName());
    }

    [TestMethod]
    public void ForEdit_KeepsOriginalId_AndIsNotNew()
    {
        var editor = SiteEditViewModel.ForEdit(Existing[0], Existing);

        Assert.IsFalse(editor.IsNew);
        Assert.AreEqual("修改站点", editor.Title);
        Assert.AreEqual("https://www.doubao.com/chat/", editor.NormalizedUrl);
    }
}
