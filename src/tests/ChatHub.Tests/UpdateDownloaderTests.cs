using ChatHub.Services;

namespace ChatHub.Tests;

[TestClass]
public sealed class UpdateDownloaderTests
{
    [TestMethod]
    public async Task DownloadAsync_NullRelease_Throws()
    {
        var downloader = new UpdateDownloader();

        try
        {
            await downloader.DownloadAsync(null!, progress: null);
            Assert.Fail("null release 应被拒绝");
        }
        catch (ArgumentNullException)
        {
            // 预期行为。
        }
    }

    [TestMethod]
    public async Task DownloadAsync_NonHttpsUrl_IsRejectedBeforeNetworkAccess()
    {
        var release = new ReleaseInfo
        {
            Version = "1.0.0",
            TagName = "v1.0.0",
            DownloadUrl = "http://example.com/win-x64.zip",
        };

        try
        {
            await new UpdateDownloader().DownloadAsync(release, progress: null);
            Assert.Fail("http 下载地址应被拒绝");
        }
        catch (InvalidOperationException)
        {
            // 预期行为：只接受 https。
        }
    }

    [TestMethod]
    public void DownloadProgress_PercentHandlesUnknownTotal()
    {
        Assert.IsNull(new DownloadProgress(1024, null).Percent);
        Assert.IsNull(new DownloadProgress(1024, 0).Percent);
        Assert.AreEqual(50.0, new DownloadProgress(512, 1024).Percent!.Value, 0.01);
        Assert.AreEqual(100.0, new DownloadProgress(2048, 1024).Percent!.Value, 0.01, "超过总长度时钳制到 100");
    }
}
