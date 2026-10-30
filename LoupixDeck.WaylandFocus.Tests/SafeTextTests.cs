namespace LoupixDeck.WaylandFocus.Tests;

public class SafeTextTests
{
    [Fact]
    public void Strip_RemovesEscapeSequences_AndTheirIntroducers()
    {
        // A hostile window title would otherwise drive the operator's
        // terminal: cursor moves, title changes, OSC 52 clipboard writes.
        var hostile = "report\e]0;spoofed-title\a\e[31mRED\e[0m\ntabbed\u007fDEL";

        var stripped = SafeText.Strip(hostile)!; // non-null input, non-null result

        Assert.DoesNotContain('\e', stripped);
        Assert.DoesNotContain('\a', stripped);
        Assert.DoesNotContain('\n', stripped);
        Assert.DoesNotContain('\t', stripped);
        Assert.DoesNotContain((char)0x7F, stripped);
        Assert.Equal("report]0;spoofed-title[31mRED[0mtabbedDEL", stripped);
    }

    [Fact]
    public void Strip_PreservesOrdinaryText()
    {
        Assert.Equal("kitty — OC | Using Obsidian", SafeText.Strip("kitty — OC | Using Obsidian"));
    }

    [Fact]
    public void Strip_PassesNullAndEmptyThrough()
    {
        Assert.Null(SafeText.Strip(null));
        Assert.Equal(string.Empty, SafeText.Strip(string.Empty));
    }
}
