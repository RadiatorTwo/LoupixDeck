using LoupixDeck.WaylandFocus.Ipc;

namespace LoupixDeck.WaylandFocus.Tests;

public class HyprlandEventTests
{
    [Fact]
    public void ParsesNameAndData()
    {
        Assert.True(HyprlandEvent.TryParse("activewindowv2>>0x55e1abcd", out var evt));
        Assert.Equal("activewindowv2", evt.Name);
        Assert.Equal("0x55e1abcd", evt.Data);
    }

    [Fact]
    public void DataMayContainSeparator()
    {
        Assert.True(HyprlandEvent.TryParse("activewindow>>class>>title", out var evt));
        Assert.Equal("activewindow", evt.Name);
        Assert.Equal("class>>title", evt.Data);
    }

    [Fact]
    public void EmptyDataIsAllowed()
    {
        Assert.True(HyprlandEvent.TryParse("activewindowv2>>", out var evt));
        Assert.Equal(string.Empty, evt.Data);
    }

    [Theory]
    [InlineData("")]
    [InlineData("noseparator")]
    [InlineData(">>leading-separator")]
    public void RejectsMalformedLines(string line)
    {
        Assert.False(HyprlandEvent.TryParse(line, out _));
    }
}
