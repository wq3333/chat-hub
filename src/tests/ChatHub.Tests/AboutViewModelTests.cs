using ChatHub.Services;
using ChatHub.ViewModels;

namespace ChatHub.Tests;

[TestClass]
public sealed class AboutViewModelTests
{
    [TestMethod]
    public async Task CheckAsync_NewerRelease_ShowsUpdateInformation()
    {
        var viewModel = new AboutViewModel("1.0.0")
        {
            CheckRelease = _ => Task.FromResult(UpdateCheckResult.Ok(CreateRelease("1.1.0", "更新说明"))),
        };

        await viewModel.CheckAsync();

        Assert.IsTrue(viewModel.HasUpdate);
        Assert.IsTrue(viewModel.HasLatestRelease);
        Assert.AreEqual("v1.1.0", viewModel.LatestVersionText);
        Assert.AreEqual("更新说明", viewModel.LatestNotes);
        StringAssert.Contains(viewModel.StatusText, "发现新版本");
        Assert.IsTrue(viewModel.UpdateCommand.CanExecute(null));
        Assert.IsFalse(viewModel.IsChecking);
    }

    [TestMethod]
    public async Task CheckAsync_SameVersion_DoesNotOfferUpdate()
    {
        var viewModel = new AboutViewModel("1.0.0")
        {
            CheckRelease = _ => Task.FromResult(UpdateCheckResult.Ok(CreateRelease("v1.0.0"))),
        };

        await viewModel.CheckAsync();

        Assert.IsFalse(viewModel.HasUpdate);
        Assert.IsTrue(viewModel.HasLatestRelease);
        StringAssert.Contains(viewModel.StatusText, "已是最新版本");
        Assert.IsFalse(viewModel.UpdateCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task CheckAsync_OlderRelease_DoesNotOfferDowngrade()
    {
        var viewModel = new AboutViewModel("1.2.0")
        {
            CheckRelease = _ => Task.FromResult(UpdateCheckResult.Ok(CreateRelease("1.0.0"))),
        };

        await viewModel.CheckAsync();

        Assert.IsFalse(viewModel.HasUpdate);
        StringAssert.Contains(viewModel.StatusText, "无需更新");
    }

    [TestMethod]
    public async Task CheckAsync_ScriptFailure_ShowsErrorAndNoUpdate()
    {
        var viewModel = new AboutViewModel("1.0.0")
        {
            CheckRelease = _ => Task.FromResult(UpdateCheckResult.Failed("No releases found")),
        };

        await viewModel.CheckAsync();

        Assert.IsFalse(viewModel.HasUpdate);
        Assert.IsFalse(viewModel.HasLatestRelease);
        StringAssert.Contains(viewModel.StatusText, "检查更新失败");
        StringAssert.Contains(viewModel.StatusText, "No releases found");
    }

    [TestMethod]
    public async Task CheckAsync_ThrownException_IsReportedInStatus()
    {
        var viewModel = new AboutViewModel("1.0.0")
        {
            CheckRelease = _ => throw new InvalidOperationException("网络不可用"),
        };

        await viewModel.CheckAsync();

        Assert.IsFalse(viewModel.HasUpdate);
        StringAssert.Contains(viewModel.StatusText, "网络不可用");
        Assert.IsFalse(viewModel.IsChecking);
    }

    [TestMethod]
    public async Task DownloadAndUpdate_Success_StartsUpgradeAndShutsDown()
    {
        var viewModel = new AboutViewModel("1.0.0")
        {
            CheckRelease = _ => Task.FromResult(UpdateCheckResult.Ok(CreateRelease("1.1.0"))),
        };
        await viewModel.CheckAsync();

        string? startedZip = null;
        var shutdown = false;
        viewModel.DownloadRelease = (_, _, _) => Task.FromResult(@"C:\fake\win-x64.zip");
        viewModel.StartUpgrade = zip => startedZip = zip;
        viewModel.ShutdownApplication = () => shutdown = true;

        await viewModel.DownloadAndUpdateAsync();

        Assert.AreEqual(@"C:\fake\win-x64.zip", startedZip);
        Assert.IsTrue(shutdown);
        Assert.IsFalse(viewModel.IsDownloading);
        StringAssert.Contains(viewModel.StatusText, "重启");
    }

    [TestMethod]
    public async Task DownloadAndUpdate_Failure_DoesNotStartUpgrade()
    {
        var viewModel = new AboutViewModel("1.0.0")
        {
            CheckRelease = _ => Task.FromResult(UpdateCheckResult.Ok(CreateRelease("1.1.0"))),
        };
        await viewModel.CheckAsync();

        var upgradeStarted = false;
        viewModel.DownloadRelease = (_, _, _) => throw new IOException("磁盘已满（测试模拟）");
        viewModel.StartUpgrade = _ => upgradeStarted = true;
        viewModel.ShutdownApplication = () => Assert.Fail("下载失败时不应关闭应用");

        await viewModel.DownloadAndUpdateAsync();

        Assert.IsFalse(upgradeStarted);
        StringAssert.Contains(viewModel.StatusText, "更新失败");
        Assert.IsFalse(viewModel.IsDownloading);
    }

    [TestMethod]
    public async Task DownloadAndUpdate_WithoutUpdate_DoesNothing()
    {
        var viewModel = new AboutViewModel("1.0.0");
        var downloadCalled = false;
        viewModel.DownloadRelease = (_, _, _) =>
        {
            downloadCalled = true;
            return Task.FromResult("unused");
        };

        await viewModel.DownloadAndUpdateAsync();

        Assert.IsFalse(downloadCalled);
    }

    [TestMethod]
    public async Task DownloadAndUpdate_WithoutUpgradeHandler_DoesNotDownload()
    {
        var viewModel = new AboutViewModel("1.0.0")
        {
            CheckRelease = _ => Task.FromResult(UpdateCheckResult.Ok(CreateRelease("1.1.0"))),
        };
        await viewModel.CheckAsync();

        var downloadCalled = false;
        viewModel.DownloadRelease = (_, _, _) =>
        {
            downloadCalled = true;
            return Task.FromResult(@"C:\fake\win-x64.zip");
        };

        await viewModel.DownloadAndUpdateAsync();

        Assert.IsFalse(downloadCalled);
        StringAssert.Contains(viewModel.StatusText, "未配置升级脚本");
    }

    [TestMethod]
    public async Task DownloadProgress_ReportsBytesAndPercentage()
    {
        // Progress<T> 会捕获创建时的 SynchronizationContext；用同步上下文让回调立即可断言。
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            var viewModel = new AboutViewModel("1.0.0")
            {
                CheckRelease = _ => Task.FromResult(UpdateCheckResult.Ok(CreateRelease("1.1.0"))),
            };
            await viewModel.CheckAsync();

            double? observedPercent = null;
            viewModel.DownloadRelease = (_, progress, _) =>
            {
                progress.Report(new DownloadProgress(512 * 1024, 1024 * 1024));
                observedPercent = viewModel.DownloadProgress;
                return Task.FromResult(@"C:\fake\win-x64.zip");
            };
            viewModel.StartUpgrade = _ => { };
            viewModel.ShutdownApplication = () => { };

            await viewModel.DownloadAndUpdateAsync();

            Assert.AreEqual(100.0, viewModel.DownloadProgress, 0.01);
            Assert.AreEqual(50.0, observedPercent!.Value, 0.01, "下载过程中应实时刷新进度");
            Assert.AreEqual("下载完成。", viewModel.DownloadStatusText);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private static ReleaseInfo CreateRelease(string version, string notes = "release notes")
    {
        var normalized = UpdateChecker.NormalizeTag(version);
        return new ReleaseInfo
        {
            Version = normalized,
            TagName = "v" + normalized,
            Name = "ChatHub v" + normalized,
            Notes = notes,
            PageUrl = $"https://github.com/wq3333/chat-hub/releases/tag/v{normalized}",
            DownloadUrl = $"https://github.com/wq3333/chat-hub/releases/download/v{normalized}/win-x64.zip",
            PublishedAt = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero),
        };
    }
}
