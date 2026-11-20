using ChatHub.Services;

namespace ChatHub.Tests;

[TestClass]
public sealed class WindowPlacementTests
{
    private const double ScreenLeft = 0;
    private const double ScreenTop = 0;
    private const double ScreenWidth = 1920;
    private const double ScreenHeight = 1080;

    [TestMethod]
    public void IsOnScreen_AcceptsFullyAndPartiallyVisibleWindows()
    {
        Assert.IsTrue(new WindowPlacement(100, 100, 1200, 820, false).IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
        Assert.IsTrue(new WindowPlacement(-500, 100, 1200, 820, false).IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
        Assert.IsTrue(new WindowPlacement(100, -500, 1200, 820, false).IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
    }

    [TestMethod]
    public void IsOnScreen_RejectsWindowsThatAreEffectivelyOffScreen()
    {
        Assert.IsFalse(new WindowPlacement(2000, 100, 1200, 820, false).IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
        Assert.IsFalse(new WindowPlacement(100, 1080, 1200, 820, false).IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
        Assert.IsFalse(new WindowPlacement(-2000, -2000, 1200, 820, false).IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
    }

    [TestMethod]
    public void ClampInto_KeepsTitleBarReachable()
    {
        var offScreen = new WindowPlacement(2000, 2000, 1200, 820, false);
        var clamped = offScreen.ClampInto(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

        Assert.AreEqual(1800.0, clamped.Left, 0.01);
        Assert.AreEqual(1032.0, clamped.Top, 0.01);
        Assert.IsTrue(clamped.IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
    }

    [TestMethod]
    public void ClampInto_LeavesOnScreenPlacementUntouched()
    {
        var onScreen = new WindowPlacement(100, 80, 1000, 700, false);
        var clamped = onScreen.ClampInto(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

        Assert.AreEqual(onScreen, clamped);
    }

    [TestMethod]
    public void IsOnScreen_RejectsInvalidBounds()
    {
        Assert.IsFalse(new WindowPlacement(double.NaN, 0, 1000, 700, false).IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
        Assert.IsFalse(new WindowPlacement(0, 0, 0, 700, false).IsOnScreen(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight));
    }
}

[TestClass]
public sealed class WindowPlacementStoreTests : TestBase
{
    private string PlacementFile => Path.Combine(TempRoot, "window.json");

    [TestMethod]
    public void Load_MissingFile_ReturnsNull()
    {
        var store = new WindowPlacementStore(PlacementFile);

        Assert.IsNull(store.Load());
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripsNormalPlacement()
    {
        var store = new WindowPlacementStore(PlacementFile);
        var expected = new WindowPlacement(120, 80, 1000, 700, false);

        store.Save(expected);

        var loaded = store.Load();
        Assert.IsNotNull(loaded);
        Assert.AreEqual(expected, loaded);
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripsMaximizedPlacement()
    {
        var store = new WindowPlacementStore(PlacementFile);
        var expected = new WindowPlacement(-300, 40, 1400, 900, true);

        store.Save(expected);

        var loaded = store.Load();
        Assert.IsNotNull(loaded);
        Assert.AreEqual(expected, loaded);
    }

    [TestMethod]
    public void Save_OverwritesExistingRecord()
    {
        var store = new WindowPlacementStore(PlacementFile);
        store.Save(new WindowPlacement(0, 0, 800, 600, false));
        store.Save(new WindowPlacement(300, 200, 1200, 820, true));

        var loaded = store.Load();
        Assert.IsNotNull(loaded);
        Assert.AreEqual(300.0, loaded.Left, 0.01);
        Assert.AreEqual(1200.0, loaded.Width, 0.01);
        Assert.IsTrue(loaded.IsMaximized);
    }

    [TestMethod]
    public void Load_CorruptJson_ReturnsNull()
    {
        Directory.CreateDirectory(TempRoot);
        File.WriteAllText(PlacementFile, "{ this is not json");

        var store = new WindowPlacementStore(PlacementFile);

        Assert.IsNull(store.Load());
    }

    [TestMethod]
    public void Load_InvalidDimensions_ReturnsNull()
    {
        Directory.CreateDirectory(TempRoot);
        File.WriteAllText(PlacementFile,
            """{"Left":0,"Top":0,"Width":10,"Height":700,"IsMaximized":false}""");

        var store = new WindowPlacementStore(PlacementFile);

        Assert.IsNull(store.Load());
    }
}
