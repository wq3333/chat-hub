using ChatHub.Models;
using ChatHub.Services;

namespace ChatHub.Tests;

[TestClass]
public sealed class UrlNormalizerTests
{
    [TestMethod]
    [DataRow("https://chat.deepseek.com/", "https://chat.deepseek.com/")]
    [DataRow("chat.deepseek.com", "https://chat.deepseek.com/")]
    [DataRow("www.doubao.com/chat/", "https://www.doubao.com/chat/")]
    [DataRow("  https://example.com/a?b=1#c  ", "https://example.com/a?b=1#c")]
    [DataRow("HTTP://EXAMPLE.COM/", "http://example.com/")]
    [DataRow("example.com:8080/path", "https://example.com:8080/path")]
    [DataRow("localhost:8080/chat", "https://localhost:8080/chat")]
    public void Normalize_ReturnsExpectedUrl(string input, string expected)
    {
        Assert.AreEqual(expected, UrlNormalizer.Normalize(input));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    [DataRow("file:///C:/Windows/System32/calc.exe")]
    [DataRow("javascript:alert(1)")]
    [DataRow("data:text/html,<h1>hi</h1>")]
    [DataRow("ftp://example.com")]
    [DataRow("https://")]
    [DataRow("随便写的中文")]
    [DataRow("内网主机名")]
    public void Normalize_RejectsInvalidInput(string? input)
    {
        Assert.IsNull(UrlNormalizer.Normalize(input));
    }

    [TestMethod]
    public void Normalize_AddsHttpsOnlyWhenSchemeMissing()
    {
        Assert.AreEqual("http://example.com/", UrlNormalizer.Normalize("http://example.com"));
        Assert.AreEqual("https://example.com/", UrlNormalizer.Normalize("example.com"));
    }

    [TestMethod]
    public void AreEquivalent_IgnoresCaseAndTrailingSlash()
    {
        Assert.IsTrue(UrlNormalizer.AreEquivalent("https://Chat.DeepSeek.com/", "chat.deepseek.com"));
        Assert.IsFalse(UrlNormalizer.AreEquivalent("https://chat.deepseek.com/", "https://www.doubao.com/chat/"));
        Assert.IsFalse(UrlNormalizer.AreEquivalent("不是网址", "https://chat.deepseek.com/"));
    }

    [TestMethod]
    public void FallbackName_UsesHostWithoutWwwPrefix()
    {
        Assert.AreEqual("doubao.com", UrlNormalizer.FallbackName("https://www.doubao.com/chat/"));
        Assert.AreEqual("chat.deepseek.com", UrlNormalizer.FallbackName("chat.deepseek.com"));
        Assert.AreEqual("未命名站点", UrlNormalizer.FallbackName("不是网址"), "非法网址不能变成站点名");
    }

    [TestMethod]
    public void ClampName_TrimsAndCaps()
    {
        Assert.AreEqual("豆包", UrlNormalizer.ClampName("  豆包  "));
        Assert.AreEqual(UrlNormalizer.MaxNameLength, UrlNormalizer.ClampName(new string('长', 100)).Length);
    }
}
