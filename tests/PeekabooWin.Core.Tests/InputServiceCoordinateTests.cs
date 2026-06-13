using PeekabooWin.Core.Infrastructure;
using PeekabooWin.Core.Input;
using Xunit;

namespace PeekabooWin.Core.Tests;

public class InputServiceCoordinateTests
{
    [Fact]
    public void Click_UsesPhysicalCoordinatesWithoutDpiScaling()
    {
        var dpi = new RecordingDpiContext(scale: 1.5);
        var service = new InputService(dpi);

        var result = service.Click(120, 80);

        Assert.False(result.Success);
        Assert.Equal((120, 80), dpi.LastBoundsCheck);
        Assert.Equal(0, dpi.PrimaryScaleCalls);
    }

    [Fact]
    public void RightClick_UsesPhysicalCoordinatesWithoutDpiScaling()
    {
        var dpi = new RecordingDpiContext(scale: 1.5);
        var service = new InputService(dpi);

        var result = service.RightClick(120, 80);

        Assert.False(result.Success);
        Assert.Equal((120, 80), dpi.LastBoundsCheck);
        Assert.Equal(0, dpi.PrimaryScaleCalls);
    }

    [Fact]
    public void ClickLogical_ExplicitlyAppliesDpiScaling()
    {
        var dpi = new RecordingDpiContext(scale: 1.5);
        var service = new InputService(dpi);

        var result = service.ClickLogical(120, 80);

        Assert.False(result.Success);
        Assert.Equal((180, 120), dpi.LastBoundsCheck);
        Assert.Equal(1, dpi.PrimaryScaleCalls);
    }

    private sealed class RecordingDpiContext : DpiContext
    {
        private readonly double _scale;

        public RecordingDpiContext(double scale)
        {
            _scale = scale;
        }

        public (int X, int Y)? LastBoundsCheck { get; private set; }

        public int PrimaryScaleCalls { get; private set; }

        public override double GetPrimaryScale()
        {
            PrimaryScaleCalls++;
            return _scale;
        }

        public override bool IsWithinScreenBounds(int x, int y)
        {
            LastBoundsCheck = (x, y);
            return false;
        }
    }
}
