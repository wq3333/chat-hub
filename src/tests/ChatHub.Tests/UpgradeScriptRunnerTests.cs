using System.Text;
using ChatHub.Services;

namespace ChatHub.Tests;

[TestClass]
public sealed class UpgradeScriptRunnerTests : TestBase
{
    [TestMethod]
    public async Task CheckAsync_ParsesJsonFromScriptOutput()
    {
        var scriptPath = WriteScript("""
            @echo off
            echo {"version":"1.0.1","tag_name":"v1.0.1","name":"ChatHub v1.0.1","body":"notes","html_url":"https://github.com/wq3333/chat-hub/releases/tag/v1.0.1","download_url":"https://github.com/wq3333/chat-hub/releases/download/v1.0.1/win-x64.zip","published_at":"2026-11-19T10:00:00Z","size":1000}
            exit /b 0
            """);

        var runner = new UpgradeScriptRunner(scriptPath);
        var result = await runner.CheckAsync();

        Assert.IsTrue(result.Success);
        Assert.AreEqual("1.0.1", result.Release!.Version);
        Assert.AreEqual("notes", result.Release.Notes);
    }

    [TestMethod]
    public async Task CheckAsync_NonZeroExitWithErrorJson_ReturnsScriptError()
    {
        var scriptPath = WriteScript("""
            @echo off
            echo {"error":"No releases found"}
            exit /b 1
            """);

        var runner = new UpgradeScriptRunner(scriptPath);
        var result = await runner.CheckAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual("No releases found", result.Error);
    }

    [TestMethod]
    public async Task CheckAsync_MissingScript_ReturnsFailure()
    {
        var missing = Path.Combine(TempRoot, "missing-upgrade.bat");
        var runner = new UpgradeScriptRunner(missing);

        var result = await runner.CheckAsync();

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "未找到升级脚本");
    }

    [TestMethod]
    public async Task CheckAsync_ScriptWithoutJson_ReportsStderr()
    {
        var scriptPath = WriteScript("""
            @echo off
            1>&2 echo updater exploded
            exit /b 1
            """);

        var runner = new UpgradeScriptRunner(scriptPath);
        var result = await runner.CheckAsync();

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "updater exploded");
    }

    /// <summary>脚本放在带空格的目录里，验证 cmd 的路径引号处理。</summary>
    private string WriteScript(string content)
    {
        var directory = Path.Combine(TempRoot, "scripts with spaces");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "upgrade.bat");
        File.WriteAllText(path, content, Encoding.ASCII);
        return path;
    }
}
