using ChatHub.Services;

namespace ChatHub.Tests;

[TestClass]
public sealed class NewWindowPolicyTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("about:blank")]
    [DataRow("ABOUT:BLANK")]
    [DataRow("https://example.com/path?q=1")]
    [DataRow("http://localhost:8080/test.html")]
    [DataRow("HTTPS://EXAMPLE.COM/")]
    public void CanOpenInApp_AllowsBlankAndHttp(string? uri)
    {
        Assert.IsTrue(NewWindowPolicy.CanOpenInApp(uri));
    }

    [TestMethod]
    [DataRow("mailto:test@example.com")]
    [DataRow("file:///C:/Windows/System32/calc.exe")]
    [DataRow("javascript:alert(1)")]
    [DataRow("data:text/html,<h1>hi</h1>")]
    [DataRow("ftp://example.com")]
    [DataRow("weixin://dl/chat")]
    [DataRow("https://")]
    [DataRow("不是网址")]
    public void CanOpenInApp_RejectsNonHttpSchemes(string uri)
    {
        Assert.IsFalse(NewWindowPolicy.CanOpenInApp(uri));
    }

    [TestMethod]
    [DataRow(-1, false)]
    [DataRow(0, true)]
    [DataRow(11, true)]
    [DataRow(12, false)]
    [DataRow(13, false)]
    public void CanOpenNewTab_RespectsLimit(int openTabs, bool expected)
    {
        Assert.AreEqual(expected, NewWindowPolicy.CanOpenNewTab(openTabs));
    }

    [TestMethod]
    [DataRow(null, "新标签页")]
    [DataRow("", "新标签页")]
    [DataRow("about:blank", "新标签页")]
    [DataRow("mailto:test@example.com", "新标签页")]
    [DataRow("https://www.doubao.com/chat/", "doubao.com")]
    [DataRow("chat.deepseek.com", "chat.deepseek.com")]
    public void InitialTitle_UsesHostOrPlaceholder(string? uri, string expected)
    {
        Assert.AreEqual(expected, NewWindowPolicy.InitialTitle(uri));
    }
}
