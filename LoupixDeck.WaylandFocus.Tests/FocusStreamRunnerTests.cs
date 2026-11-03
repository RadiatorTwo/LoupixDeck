using System.Net.Sockets;

namespace LoupixDeck.WaylandFocus.Tests;

/// <summary>
/// Pins the runner's two policies: a stream that never worked is a setup failure with
/// no retries, a working stream that dies is retried on a capped backoff until the
/// budget runs out, and cancellation is always the clean exit.
/// </summary>
public class FocusStreamRunnerTests
{
    [Fact]
    public async Task RunAsync_FirstStreamEnds_ReconnectsAndKeepsEvents()
    {
        List<string> log = [];
        List<TimeSpan> delays = [];
        List<FocusInfo?> received = [];
        using CancellationTokenSource cts = new();
        int factoryCalls = 0;

        FocusStreamRunner runner = new(
            () =>
            {
                factoryCalls++;
                return factoryCalls == 1
                    ? new FakeFocusSource("fake", () => Emit(
                        new FocusInfo("a", "one", null),
                        new FocusInfo("a", "two", null)))
                    : new FakeFocusSource("fake", () => Emit(
                        new FocusInfo("b", "one", null)));
            },
            log.Add,
            maxAttempts: 5,
            backoff: attempt => TimeSpan.FromSeconds(attempt),
            delay: (wait, ct) =>
            {
                delays.Add(wait);
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        // The third event cancels; the second stream then ends, and the following
        // backoff observes the token.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            focus =>
            {
                received.Add(focus);
                if (received.Count >= 3)
                {
                    cts.Cancel();
                }
            },
            cts.Token));

        Assert.Equal(3, received.Count);
        Assert.Equal("b", received[2]!.AppId);
        Assert.Equal(2, factoryCalls);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
        Assert.Contains(log, line => line.Contains("Wayland monitor active (compositor: fake)."));
        Assert.Contains(log, line => line.Contains("reconnecting in 1s (attempt 1/5)."));
        Assert.Contains(log, line => line.Contains("Wayland monitor reconnected (compositor: fake)."));
    }

    [Fact]
    public async Task RunAsync_SourceFaultsBeforeFirstEvent_ThrowsWithoutRetrying()
    {
        List<string> log = [];
        List<TimeSpan> delays = [];
        int factoryCalls = 0;

        FocusStreamRunner runner = new(
            () =>
            {
                factoryCalls++;
                return new FakeFocusSource("fake",
                    () => throw new IOException("Connection refused"));
            },
            log.Add,
            maxAttempts: 5,
            backoff: attempt => TimeSpan.FromSeconds(attempt),
            delay: (wait, _) =>
            {
                delays.Add(wait);
                return Task.CompletedTask;
            });

        FocusStreamFailedException failure =
            await Assert.ThrowsAsync<FocusStreamFailedException>(
                () => runner.RunAsync(_ => { }, CancellationToken.None));

        Assert.Contains("could not start", failure.Message);
        Assert.Contains("Connection refused", failure.Message);
        Assert.Equal(1, factoryCalls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task RunAsync_EmptySource_ThrowsWithoutRetrying()
    {
        List<TimeSpan> delays = [];
        int factoryCalls = 0;

        FocusStreamRunner runner = new(
            () =>
            {
                factoryCalls++;
                return new FakeFocusSource("fake", () => Emit());
            },
            log: null,
            maxAttempts: 5,
            backoff: attempt => TimeSpan.FromSeconds(attempt),
            delay: (wait, _) =>
            {
                delays.Add(wait);
                return Task.CompletedTask;
            });

        FocusStreamFailedException failure =
            await Assert.ThrowsAsync<FocusStreamFailedException>(
                () => runner.RunAsync(_ => { }, CancellationToken.None));

        Assert.Contains("ended before reporting", failure.Message);
        Assert.Equal(1, factoryCalls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task RunAsync_StreamDiesAfterEvents_IsRetried()
    {
        List<string> log = [];
        List<TimeSpan> delays = [];
        List<FocusInfo?> received = [];
        using CancellationTokenSource cts = new();
        int factoryCalls = 0;

        FocusStreamRunner runner = new(
            () =>
            {
                factoryCalls++;
                return factoryCalls == 1
                    ? new FakeFocusSource("fake", () => EmitThenThrow(
                        [new FocusInfo("a", "one", null)], new IOException("boom")))
                    : new FakeFocusSource("fake", () => Emit(
                        new FocusInfo("a", "two", null)));
            },
            log.Add,
            maxAttempts: 5,
            backoff: attempt => TimeSpan.FromSeconds(attempt),
            delay: (wait, ct) =>
            {
                delays.Add(wait);
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            focus =>
            {
                received.Add(focus);
                if (received.Count >= 2)
                {
                    cts.Cancel();
                }
            },
            cts.Token));

        Assert.Equal(2, received.Count);
        Assert.Equal(2, factoryCalls);
        // One delay to reach the second source; its completion (the scripted source
        // does not observe the token) queues the next delay, which raises the OCE.
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
        // The runtime death is named in the reconnect line.
        Assert.Contains(log, line => line.Contains("boom") && line.Contains("reconnecting"));
    }

    [Fact]
    public async Task RunAsync_GivesUpAfterMaxAttempts()
    {
        List<TimeSpan> delays = [];
        int factoryCalls = 0;

        FocusStreamRunner runner = new(
            () =>
            {
                // Every source reports its initial focus and then ends: a working
                // stream that never comes back is what exhausts the budget.
                factoryCalls++;
                return new FakeFocusSource("fake", () => Emit(
                    new FocusInfo("a", "t", null)));
            },
            log: null,
            maxAttempts: 2,
            backoff: attempt => TimeSpan.FromSeconds(attempt),
            delay: (wait, _) =>
            {
                delays.Add(wait);
                return Task.CompletedTask;
            });

        FocusStreamFailedException failure =
            await Assert.ThrowsAsync<FocusStreamFailedException>(
                () => runner.RunAsync(_ => { }, CancellationToken.None));

        Assert.Contains("did not come back after 2 attempts", failure.Message);
        Assert.Equal(3, factoryCalls); // initial source + two retries
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
    }

    [Fact]
    public async Task RunAsync_CancellationDuringBackoff_Propagates()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        int factoryCalls = 0;

        FocusStreamRunner runner = new(
            () =>
            {
                // Yields once, so the runner reaches the backoff where the already
                // cancelled token must surface as OCE.
                factoryCalls++;
                return new FakeFocusSource("fake", () => Emit(
                    new FocusInfo("a", "t", null)));
            },
            log: null,
            maxAttempts: 5,
            backoff: attempt => TimeSpan.FromSeconds(attempt),
            delay: (_, ct) => Task.Delay(Timeout.Infinite, ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(_ => { }, cts.Token));

        Assert.Equal(1, factoryCalls);
    }

    // ─── scripted sources ───

    private sealed class FakeFocusSource(string name, Func<IAsyncEnumerable<FocusInfo?>> body)
        : IFocusSource
    {
        public string Compositor => name;

        public IAsyncEnumerable<FocusInfo?> WatchAsync(
            CancellationToken cancellationToken = default) => body();
    }

    private static async IAsyncEnumerable<FocusInfo?> Emit(params FocusInfo?[] events)
    {
        foreach (FocusInfo? focus in events)
        {
            yield return focus;
        }

        await Task.CompletedTask; // keeps the async iterator honest without changing timing
    }

    private static async IAsyncEnumerable<FocusInfo?> EmitThenThrow(
        IReadOnlyList<FocusInfo?> events, Exception failure)
    {
        foreach (FocusInfo? focus in events)
        {
            yield return focus;
        }

        await Task.CompletedTask;
        throw failure;
    }
}