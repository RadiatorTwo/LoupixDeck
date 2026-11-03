namespace LoupixDeck.WaylandFocus.Tests;

/// <summary>
/// Locks down the two promises of <see cref="FocusEventGate"/>: resolution happens
/// once per app_id and never for PID-bearing events, and the published stream keeps
/// (process, title) dedup semantics.
/// </summary>
public class FocusEventGateTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Take_PidlessTitleChurn_ResolvesOnceAndPublishesEveryTitle()
    {
        int calls = 0;
        FocusEventGate gate = new(
            _ => { calls++; return "code"; },
            () => T0);

        FocusInfo? first = gate.Take(new FocusInfo("code", "editor 1", null));
        FocusInfo? second = gate.Take(new FocusInfo("code", "editor 2 - file.cs", null));
        FocusInfo? third = gate.Take(new FocusInfo("code", "editor 3 - Program.cs", null));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal("code", first!.ProcessName);
        Assert.Equal("code", second!.ProcessName);
        Assert.Equal("code", third!.ProcessName);
        // The headline: three title changes cost one scan, not three.
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Take_DuplicateEvent_ReturnsNullWithoutResolving()
    {
        int calls = 0;
        FocusEventGate gate = new(
            _ => { calls++; return "foot"; },
            () => T0);

        Assert.NotNull(gate.Take(new FocusInfo("foot", "top", null)));
        Assert.Null(gate.Take(new FocusInfo("foot", "top", null)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Take_AppIdSwitchAwayAndBack_ResolvesEachAppOnce()
    {
        int calls = 0;
        FocusEventGate gate = new(
            focus => { calls++; return focus.AppId == "a" ? "alpha" : "beta"; },
            () => T0);

        Assert.NotNull(gate.Take(new FocusInfo("a", "window", null)));
        Assert.NotNull(gate.Take(new FocusInfo("b", "window", null)));
        Assert.NotNull(gate.Take(new FocusInfo("a", "window", null)));

        // "a" resolved on the way out, cached on the way back.
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Take_DifferentAppIdsSameBinary_StillDeduplicatesOnProcessAndTitle()
    {
        int calls = 0;
        FocusEventGate gate = new(
            _ => { calls++; return "sh"; },
            () => T0);

        Assert.NotNull(gate.Take(new FocusInfo("app.one", "prompt", null)));
        // Same resolved binary and title: the (process, title) dedup suppresses it,
        // even though the app_id - and therefore the resolution - differed.
        Assert.Null(gate.Take(new FocusInfo("app.two", "prompt", null)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Take_PidBearingEvent_AlwaysResolvesLive()
    {
        int calls = 0;
        FocusEventGate gate = new(
            _ => { calls++; return "kitty"; },
            () => T0);

        Assert.NotNull(gate.Take(new FocusInfo("foot", "one", 100)));
        Assert.NotNull(gate.Take(new FocusInfo("foot", "two", 100)));

        // Same app_id twice, PID set: a PID is ground truth, so the cache is bypassed
        // and every event pays the cheap live read - two calls, not one.
        Assert.Equal(2, calls);
        Assert.Null(gate.Take(new FocusInfo("foot", "two", 100)));
    }

    [Fact]
    public void Take_EmptyAppId_IsNeverCached()
    {
        int calls = 0;
        FocusEventGate gate = new(
            _ => { calls++; return null; },
            () => T0);

        Assert.NotNull(gate.Take(new FocusInfo("", "first", null)));
        Assert.NotNull(gate.Take(new FocusInfo("", "second", null)));

        // No key to trust, so each event resolves again.
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Take_UnresolvableApp_CachesMissUntilTtlExpiry()
    {
        int calls = 0;
        DateTime now = T0;
        FocusEventGate gate = new(
            _ => { calls++; return null; },
            () => now);

        Assert.NotNull(gate.Take(new FocusInfo("ghost", "one", null)));
        // The structural miss is cached: a title change must not rescan.
        Assert.NotNull(gate.Take(new FocusInfo("ghost", "two", null)));
        Assert.Equal(1, calls);

        // Expiry heals it - the promise that lets newly installed apps resolve later.
        now = now.AddMinutes(6);
        Assert.NotNull(gate.Take(new FocusInfo("ghost", "three", null)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Take_CapacityOverflow_DropsCachedEntries()
    {
        int calls = 0;
        FocusEventGate gate = new(
            focus => { calls++; return focus.AppId + "-proc"; },
            () => T0,
            capacity: 2);

        Assert.NotNull(gate.Take(new FocusInfo("a", "t", null))); // resolve 1, cached
        Assert.NotNull(gate.Take(new FocusInfo("b", "t", null))); // resolve 2, cached
        Assert.NotNull(gate.Take(new FocusInfo("c", "t", null))); // cap hit: clear, resolve 3

        // "a" was dropped by the overflow, so returning to it resolves again instead of
        // growing the dictionary past its bound.
        Assert.NotNull(gate.Take(new FocusInfo("a", "t2", null)));
        Assert.Equal(4, calls);
    }
}