using ChatHub.Services;

namespace ChatHub.Tests;

[TestClass]
public sealed class UpdateCheckResultTests
{
    private const string ValidJson = """
        {"version":"1.0.1","tag_name":"v1.0.1","name":"ChatHub v1.0.1","body":"第一行\n第二行","html_url":"https://github.com/wq3333/chat-hub/releases/tag/v1.0.1","download_url":"https://github.com/wq3333/chat-hub/releases/download/v1.0.1/win-x64.zip","published_at":"2026-11-19T10:00:00Z","size":123456}
        """;

    [TestMethod]
    public void ParseCheckResponse_ReadsAllFields()
    {
        var result = UpdateChecker.ParseCheckResponse(ValidJson);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Release);
        Assert.AreEqual("1.0.1", result.Release.Version);
        Assert.AreEqual("v1.0.1", result.Release.TagName);
        Assert.AreEqual("ChatHub v1.0.1", result.Release.Name);
        StringAssert.Contains(result.Release.Notes, "第一行");
        StringAssert.Contains(result.Release.Notes, "第二行");
        Assert.AreEqual(
            "https://github.com/wq3333/chat-hub/releases/download/v1.0.1/win-x64.zip",
            result.Release.DownloadUrl);
        Assert.AreEqual(123456L, result.Release.AssetSize);
        Assert.IsNotNull(result.Release.PublishedAt);
    }

    [TestMethod]
    public void ParseCheckResponse_ErrorObjectBecomesFailure()
    {
        var result = UpdateChecker.ParseCheckResponse("""{"error":"Not Found"}""");

        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Release);
        Assert.AreEqual("Not Found", result.Error);
    }

    [TestMethod]
    public void ParseCheckResponse_RejectsMissingDownloadUrl()
    {
        var json = ValidJson.Replace(
            @"""download_url"":""https://github.com/wq3333/chat-hub/releases/download/v1.0.1/win-x64.zip""",
            @"""download_url"":null");

        var result = UpdateChecker.ParseCheckResponse(json);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "下载地址");
    }

    [TestMethod]
    public void ParseCheckResponse_RejectsNonHttpsDownloadUrl()
    {
        var json = ValidJson.Replace(
            "https://github.com/wq3333/chat-hub/releases/download/v1.0.1/win-x64.zip",
            "http://example.com/win-x64.zip");

        var result = UpdateChecker.ParseCheckResponse(json);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "https");
    }

    [TestMethod]
    public void ParseCheckResponse_RejectsMissingVersion()
    {
        var json = """{"tag_name":"","download_url":"https://example.com/a.zip"}""";

        var result = UpdateChecker.ParseCheckResponse(json);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "版本号");
    }

    [TestMethod]
    public void ParseCheckResponse_ToleratesEmptyAndGarbageInput()
    {
        Assert.IsFalse(UpdateChecker.ParseCheckResponse(string.Empty).Success);
        Assert.IsFalse(UpdateChecker.ParseCheckResponse("<not json>").Success);
        Assert.IsFalse(UpdateChecker.ParseCheckResponse(null).Success);
        StringAssert.Contains(UpdateChecker.ParseCheckResponse("oops").Error, "解析");
    }

    [TestMethod]
    public void ParseCheckResponse_FallsBackToTagAndReleasePage()
    {
        var json = """{"tag_name":"v2.0.0","download_url":"https://example.com/a.zip"}""";

        var result = UpdateChecker.ParseCheckResponse(json);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("2.0.0", result.Release!.Version);
        Assert.AreEqual(AppInfo.ReleasesUrl, result.Release.PageUrl);
        Assert.IsNull(result.Release.AssetSize);
    }
}
