using MyMelody.App.Services;
using Xunit;

namespace MyMelody.UpdateTests;

public sealed class DesktopPlacementTests
{
    private static readonly PixelBounds Primary = new(0, 0, 3440, 1392);
    private static readonly PixelBounds LeftMonitor = new(-2560, 0, 0, 1392);

    [Theory]
    [InlineData(-100, 300, 0, 300)]
    [InlineData(3400, 300, 3344, 300)]
    [InlineData(300, -200, 300, 0)]
    [InlineData(300, 1420, 300, 1296)]
    [InlineData(-300, -400, 0, 0)]
    [InlineData(4000, 2000, 3344, 1296)]
    public void EachEdgeAndCornerReturnsToTheRealWorkArea(int left, int top, int expectedLeft, int expectedTop)
    {
        var result = DesktopPlacement.Clamp(Square(left, top, 96), [Primary]);
        Assert.Equal(Square(expectedLeft, expectedTop, 96), result);
    }

    [Fact]
    public void NegativeMonitorCoordinatesAndCrossMonitorMovementArePreserved()
    {
        var onLeft = Square(-1500, 600, 144);
        Assert.Equal(onLeft, DesktopPlacement.Clamp(onLeft, [LeftMonitor, Primary]));
        var acrossBoundary = Square(-20, 600, 144);
        Assert.Equal(Square(0, 600, 144), DesktopPlacement.Clamp(acrossBoundary, [LeftMonitor, Primary]));
        var farLeft = Square(-5000, -300, 144);
        Assert.Equal(Square(-2560, 0, 144), DesktopPlacement.Clamp(farLeft, [LeftMonitor, Primary]));
    }

    [Fact]
    public void VerticallyStackedMonitorsAllowTransferAndClampAgainstTheirOwnTaskbars()
    {
        PixelBounds above = new(0, -1200, 1920, -40);
        PixelBounds below = new(0, 1080, 1920, 2120);
        PixelBounds middle = new(0, 0, 1920, 1040);
        Assert.Equal(Square(200, -900, 96), DesktopPlacement.Clamp(Square(200, -900, 96), [above, middle, below]));
        Assert.Equal(Square(200, 1080, 96), DesktopPlacement.Clamp(Square(200, 1060, 96), [above, middle, below]));
        Assert.Equal(Square(200, -136, 96), DesktopPlacement.Clamp(Square(200, -100, 96), [above, middle, below]));
        Assert.Equal(Square(200, 2024, 96), DesktopPlacement.Clamp(Square(200, 3000, 96), [above, middle, below]));
    }

    [Fact]
    public void EmptyDesktopGapsDoNotCountAsVisibleScreen()
    {
        PixelBounds lowerLeft = new(-1920, 500, 0, 1540);
        PixelBounds upperRight = new(0, -900, 1600, 0);
        var inGap = Square(200, 300, 96);
        Assert.Equal(Square(-96, 500, 96), DesktopPlacement.Clamp(inGap, [lowerLeft, upperRight]));
        var diagonalGap = Square(-200, -300, 96);
        Assert.Equal(Square(0, -300, 96), DesktopPlacement.Clamp(diagonalGap, [lowerLeft, upperRight]));
    }

    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(96, 1.5)]
    [InlineData(96, 2.0)]
    [InlineData(384, 1.0)]
    [InlineData(384, 1.5)]
    [InlineData(384, 2.0)]
    public void DpiChangesOnlyPhysicalSizeAndNeverScaleTheMonitorOrigin(int dipSize, double scale)
    {
        int pixels = (int)Math.Round(dipSize * scale);
        PixelBounds highDpiLeft = new(-2560, -1440, 0, -48);
        var result = DesktopPlacement.Clamp(Square(-30, -70, pixels), [highDpiLeft]);
        Assert.Equal(Square(-pixels, -48 - pixels, pixels), result);
        AssertInside(result, highDpiLeft);
    }

    [Fact]
    public void WorkAreasWithTopOrSideTaskbarsKeepTheEntireCharacterAccessible()
    {
        PixelBounds area = new(80, 48, 1920, 1080);
        var result = DesktopPlacement.Clamp(Square(0, 0, 192), [area]);
        Assert.Equal(Square(80, 48, 192), result);
        AssertInside(result, area);
    }

    [Fact]
    public void OversizedWindowKeepsItsTopLeftReachableWithoutInvalidClampRanges()
    {
        PixelBounds tiny = new(-400, -300, -100, -100);
        var result = DesktopPlacement.Clamp(Square(-200, -150, 768), [tiny]);
        Assert.Equal(Square(-400, -300, 768), result);
    }

    [Fact]
    public void ARemovedMonitorPositionReturnsToTheRemainingMonitorAndClampingIsStable()
    {
        var result = DesktopPlacement.Clamp(Square(-2000, 500, 384), [Primary]);
        Assert.Equal(Square(0, 500, 384), result);
        Assert.Equal(result, DesktopPlacement.Clamp(result, [Primary]));
    }

    private static PixelBounds Square(int left, int top, int size) => new(left, top, left + size, top + size);
    private static void AssertInside(PixelBounds window, PixelBounds area)
    {
        Assert.True(window.Left >= area.Left && window.Top >= area.Top);
        Assert.True(window.Right <= area.Right && window.Bottom <= area.Bottom);
    }
}
