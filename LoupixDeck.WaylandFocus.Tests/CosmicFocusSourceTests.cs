using LoupixDeck.WaylandFocus.Sources;

namespace LoupixDeck.WaylandFocus.Tests;

public class CosmicFocusSourceTests
{
    [Fact]
    public async Task ReportsInitialFocusThenFollowsSwitchesAndClosures()
    {
        using FakeCosmicCompositor server = new();
        CosmicFocusSource source = new(server.SocketPath);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        await using IAsyncEnumerator<FocusInfo?> enumerator = source.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

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
        using FakeCosmicCompositor server = new();
        CosmicFocusSource source = new(server.SocketPath);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        await using IAsyncEnumerator<FocusInfo?> enumerator = source.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await enumerator.MoveNextAsync()); // app.one

        // A redundant state event for the already-focused handle is deduplicated.
        server.SendRedundantState(FakeCosmicCompositor.HandleOne);

        Task<bool> next = enumerator.MoveNextAsync().AsTask();
        Task finished = await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(next, finished); // still waiting: no event was emitted

        // Shut the pending read down cleanly before the enumerator is disposed.
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }

    [Fact]
    public async Task TwoActivatedHandles_TheLaterActivationWins()
    {
        using FakeCosmicCompositor server = new();
        CosmicFocusSource source = new(server.SocketPath);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        await using IAsyncEnumerator<FocusInfo?> enumerator = source.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);
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
        using FakeCosmicCompositor server = new() { StallBeforeInitialBatch = true };
        CosmicFocusSource source = new(
            server.SocketPath, setupTimeout: TimeSpan.FromMilliseconds(300));
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        await using IAsyncEnumerator<FocusInfo?> enumerator = source.WatchAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        await Assert.ThrowsAsync<TimeoutException>(
            async () => await enumerator.MoveNextAsync());
    }
}