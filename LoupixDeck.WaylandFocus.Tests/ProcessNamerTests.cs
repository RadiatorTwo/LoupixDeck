using LoupixDeck.WaylandFocus;

namespace LoupixDeck.WaylandFocus.Tests;

public class ProcessNamerTests
{
    [Fact]
    public void NameFromPid_ReadsCommFromProcEntry()
    {
        using var proc = new FakeProc().AddProcess(42, "myapp", "/usr/bin/myapp", "--flag");

        Assert.Equal("myapp", ProcessNamer.NameFromPid(proc.Root, 42));
    }

    [Fact]
    public void NameFromPid_FallsBackToCmdlineBasename_WhenCommEmpty()
    {
        using var proc = new FakeProc().AddProcessRaw(42, "", "/usr/bin/cli-tool\0");

        Assert.Equal("cli-tool", ProcessNamer.NameFromPid(proc.Root, 42));
    }

    [Fact]
    public void NameFromPid_ReturnsNullForUnknownProcess()
    {
        using var proc = new FakeProc();

        Assert.Null(ProcessNamer.NameFromPid(proc.Root, 999999));
    }

    [Fact]
    public void Enrich_UsesTheCompositorPid_WhenTheProcessIsAlive()
    {
        using var proc = new FakeProc().AddProcess(111, "kitty", "kitty");

        var enriched = ProcessNamer.Enrich(
            new FocusInfo("kitty", "Some window", Pid: 111), proc.Root, []);

        Assert.Equal(111, enriched.Pid);
        Assert.Equal("kitty", enriched.ProcessName);
    }

    [Fact]
    public void Enrich_StalePid_IsDroppedInsteadOfMixedWithAnotherProcess()
    {
        // The compositor reported pid 999999, but that process is gone
        // (possibly recycled). The name must not come from a different
        // process while the stale pid is kept: both halves of the
        // identity come from the same process or neither.
        using var proc = new FakeProc()
            .AddProcess(111, "one", "one")
            .AddProcess(222, "two", "two");

        var enriched = ProcessNamer.Enrich(
            new FocusInfo("app.one", "Window", Pid: 999999), proc.Root, []);

        Assert.Equal(111, enriched.Pid); // matched process's own pid
        Assert.Equal("one", enriched.ProcessName);
        Assert.NotEqual(999999, enriched.Pid);
    }

    [Fact]
    public void Enrich_UnresolvableEvent_FallsBackToAppIdAsName()
    {
        using var proc = new FakeProc();

        var enriched = ProcessNamer.Enrich(
            new FocusInfo("mystery.app", "Window", Pid: null), proc.Root, []);

        Assert.Null(enriched.Pid);
        Assert.Equal("mystery.app", enriched.ProcessName);
    }

    [Theory]
    [InlineData("com.system76.CosmicTerm", "cosmic-term")]
    [InlineData("Alacritty", "alacritty")]
    [InlineData("org.gnome.Nautilus", "nautilus")]
    [InlineData("kitty", "kitty")]
    [InlineData("firefox", "firefox")]
    public void Candidates_MatchTypicalBinaryNames(string appId, string expectedBinary)
    {
        Assert.Contains(
            ProcessNamer.Candidates(appId),
            candidate => ProcessNamer.Normalize(candidate) == ProcessNamer.Normalize(expectedBinary));
    }

    [Fact]
    public void Candidates_ProduceProperKebabCase()
    {
        Assert.Contains("Cosmic-Term", ProcessNamer.Candidates("com.system76.CosmicTerm"));
    }

    [Fact]
    public void Normalize_StripsSeparatorsAndCase()
    {
        Assert.Equal("cosmicterm", ProcessNamer.Normalize("Cosmic-Term"));
        Assert.Equal("comsystem76cosmicterm", ProcessNamer.Normalize("com.system76.CosmicTerm"));
    }
}
