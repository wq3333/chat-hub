using System.Reflection;
using ChatHub.Services;

namespace ChatHub.Tests;

[TestClass]
public sealed class UpdateVersionTests
{
    [TestMethod]
    [DataRow("v1.0.0", "1.0.0")]
    [DataRow("V1.2.3", "1.2.3")]
    [DataRow("1.0.0", "1.0.0")]
    [DataRow("  v1.0.0  ", "1.0.0")]
    [DataRow("vv1.0.0", "1.0.0")]
    [DataRow("", "")]
    [DataRow(null, "")]
    public void NormalizeTag_StripsLeadingV(string? input, string expected) =>
        Assert.AreEqual(expected, UpdateChecker.NormalizeTag(input));

    [TestMethod]
    public void IsNewer_DetectsNewerVersion()
    {
        Assert.IsTrue(UpdateChecker.IsNewer("v1.0.1", "1.0.0"));
        Assert.IsTrue(UpdateChecker.IsNewer("1.2.0", "1.1.9"));
        Assert.IsTrue(UpdateChecker.IsNewer("2.0.0", "1.99.99"));
        Assert.IsTrue(UpdateChecker.IsNewer("1.0.0.1", "1.0.0"));
    }

    [TestMethod]
    public void IsNewer_ReturnsFalseForSameOrOlder()
    {
        Assert.IsFalse(UpdateChecker.IsNewer("v1.0.0", "1.0.0"));
        Assert.IsFalse(UpdateChecker.IsNewer("1.0", "1.0.0"), "补齐到 4 段后 1.0 与 1.0.0 相同");
        Assert.IsFalse(UpdateChecker.IsNewer("0.9.9", "1.0.0"));
    }

    [TestMethod]
    public void IsNewer_IgnoresPrereleaseSuffix()
    {
        Assert.IsTrue(UpdateChecker.IsNewer("v1.1.0-beta.2", "1.0.0"));
    }

    [TestMethod]
    public void IsNewer_UnparsableVersionsNeverPromptUpdate()
    {
        Assert.IsFalse(UpdateChecker.IsNewer("latest", "1.0.0"));
        Assert.IsFalse(UpdateChecker.IsNewer("v1.0.0", "unknown"));
        Assert.IsFalse(UpdateChecker.IsNewer(null, "1.0.0"));
    }

    [TestMethod]
    public void Compare_ReturnsNumericOrdering()
    {
        Assert.AreEqual(1, UpdateChecker.Compare("1.0.1", "1.0.0"));
        Assert.AreEqual(0, UpdateChecker.Compare("v1.0", "1.0.0.0"));
        Assert.AreEqual(-1, UpdateChecker.Compare("0.9", "1.0"));
        Assert.IsNull(UpdateChecker.Compare("nope", "1.0"));
    }

    [TestMethod]
    public void CurrentVersion_IsNormalizedFromInformationalVersion()
    {
        Assert.AreEqual("1.0.0", AppInfo.NormalizeDisplayVersion("1.0.0+abcdef"));
        Assert.AreEqual("1.0.0", AppInfo.NormalizeDisplayVersion(" 1.0.0 "));
        Assert.AreEqual("0.0.0", AppInfo.NormalizeDisplayVersion(null));
    }

    [TestMethod]
    public void CurrentVersion_MatchesAssemblyInformation()
    {
        var assembly = typeof(AppInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.IsFalse(string.IsNullOrWhiteSpace(informational), "csproj 的 <Version> 应生成 AssemblyInformationalVersion");
        Assert.AreEqual(AppInfo.NormalizeDisplayVersion(informational), AppInfo.CurrentVersion);
        Assert.IsTrue(UpdateChecker.TryParseNumericVersion(AppInfo.CurrentVersion, out _));
    }
}
