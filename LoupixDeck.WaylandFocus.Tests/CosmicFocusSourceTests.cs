using LoupixDeck.WaylandFocus.Sources;

namespace LoupixDeck.WaylandFocus.Tests;

public class CosmicFocusSourceTests
{
    [Fact]
    public async Task ReportsInitialFocusThenFollowsSwitchesAndClosures()
    {
        using var server = new FakeCosmicCompositor();
        var source = new CosmicFocusSource(server.SocketPath);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var enumerator = source.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        // Initial state: handle one is activated.
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(new FocusInfo("app.one", "First", Pid: null), enumerator.Current);

        // The scripted focus switch moves activation to handle two.
        server.SwitchFocus();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(new FocusInfo("app.two", "Second", Pid: null), enumerator.Current);

        // Closing the focused toplevel reports "no focused toplevel".
        server.CloseFocusedHandle();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Null(enumerator.Current);
    }

    [Fact]
    public async Task DuplicateStates_DoNotProduceExtraEvents()
    {
        using var server = new FakeCosmicCompositor();
        var source = new CosmicFocusSource(server.SocketPath);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var enumerator = source.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await enumerator.MoveNextAsync()); // app.one

        // A redundant state event for the already-focused handle is deduplicated.
        server.SendRedundantState(FakeCosmicCompositor.HandleOne);

        var next = enumerator.MoveNextAsync().AsTask();
        var finished = await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(next, finished); // still waiting: no event was emitted

        // Shut the pending read down cleanly before the enumerator is disposed.
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }

    [Fact]
    public async Task TwoActivatedHandles_TheLaterActivationWins()
    {
        using var server = new FakeCosmicCompositor();
        var source = new CosmicFocusSource(server.SocketPath);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var enumerator = source.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await enumerator.MoveNextAsync()); // app.one

        // A buggy/hostile batch that activates both handles must resolve
        // deterministically to the later activation, not dictionary order.
        server.ActivateBothHandles();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("app.two", enumerator.Current!.AppId);
    }

    [Fact]
    public async Task StalledHandshake_FailsWithTimeout_InsteadOfHangingForever()
    {
        using var server = new FakeCosmicCompositor { StallBeforeInitialBatch = true };
        var source = new CosmicFocusSource(
            server.SocketPath, setupTimeout: TimeSpan.FromMilliseconds(300));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var enumerator = source.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        await Assert.ThrowsAsync<TimeoutException>(
            async () => await enumerator.MoveNextAsync());
    }
}
