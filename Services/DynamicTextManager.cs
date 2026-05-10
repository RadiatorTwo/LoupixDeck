using Avalonia.Threading;
using LoupixDeck.Commands.Base;
using LoupixDeck.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LoupixDeck.Services;

public interface IDynamicTextManager
{
    void Start();
    void Rescan();
}

public class DynamicTextManager : IDynamicTextManager, IDisposable
{
    private sealed class Entry
    {
        public TouchButton Button;
        public IDynamicTextProvider Provider;
        public string[] Parameters;
        public TimeSpan Interval;
        public DateTime NextDueUtc;
    }

    private readonly IPageManager _pageManager;
    private readonly ISysCommandService _sysCommandService;
    private readonly IServiceProvider _serviceProvider;

    private readonly object _gate = new();
    private List<Entry> _active = new();
    private CancellationTokenSource _cts;
    private PeriodicTimer _timer;
    private Task _loopTask;

    public DynamicTextManager(
        IPageManager pageManager,
        ISysCommandService sysCommandService,
        IServiceProvider serviceProvider)
    {
        _pageManager = pageManager;
        _sysCommandService = sysCommandService;
        _serviceProvider = serviceProvider;
    }

    public void Start()
    {
        _pageManager.OnTouchPageChanged += OnTouchPageChanged;
        Rescan();
    }

    private void OnTouchPageChanged(int previous, int current) => Rescan();

    public void Rescan()
    {
        StopLoop();

        var page = _pageManager.CurrentTouchButtonPage;
        var entries = new List<Entry>();

        if (page?.TouchButtons != null)
        {
            foreach (var button in page.TouchButtons)
            {
                if (button == null || string.IsNullOrWhiteSpace(button.Command))
                    continue;

                var name = ParseCommandName(button.Command);
                if (string.IsNullOrEmpty(name))
                    continue;

                if (!_sysCommandService.TryGetCommandType(name, out var type))
                    continue;

                if (!typeof(IDynamicTextProvider).IsAssignableFrom(type))
                    continue;

                IDynamicTextProvider provider;
                try
                {
                    provider = (IDynamicTextProvider)ActivatorUtilities.CreateInstance(_serviceProvider, type);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DynamicTextManager: failed to instantiate '{name}': {ex.Message}");
                    continue;
                }

                var parms = ParseParameters(button.Command);
                var interval = provider.UpdateInterval;
                if (interval < TimeSpan.FromMilliseconds(250))
                    interval = TimeSpan.FromMilliseconds(250);

                entries.Add(new Entry
                {
                    Button = button,
                    Provider = provider,
                    Parameters = parms,
                    Interval = interval,
                    NextDueUtc = DateTime.UtcNow
                });
            }
        }

        if (entries.Count == 0)
            return;

        var minInterval = entries.Min(e => e.Interval);
        // Tick faster than the smallest interval so wall-clock-aligned NextDue
        // boundaries are hit promptly (perceived smoothness for the clock).
        var tickInterval = TimeSpan.FromTicks(minInterval.Ticks / 4);
        if (tickInterval < TimeSpan.FromMilliseconds(100))
            tickInterval = TimeSpan.FromMilliseconds(100);
        if (tickInterval > minInterval)
            tickInterval = minInterval;

        // Pre-align each entry's first NextDue to the next wall-clock interval boundary
        // so e.g. a 1s clock fires exactly when the wall-clock second rolls over.
        var nowAlign = DateTime.UtcNow;
        foreach (var entry in entries)
        {
            entry.NextDueUtc = AlignedNext(nowAlign, entry.Interval);
        }

        lock (_gate)
        {
            _active = entries;
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(tickInterval);
            var token = _cts.Token;
            var timer = _timer;
            _loopTask = Task.Run(() => TickLoop(timer, token), token);
        }
    }

    private static DateTime AlignedNext(DateTime from, TimeSpan interval)
    {
        var ticks = interval.Ticks;
        if (ticks <= 0) return from;
        var next = ((from.Ticks / ticks) + 1) * ticks;
        return new DateTime(next, from.Kind);
    }

    private async Task TickLoop(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            // Fire once immediately so dynamic buttons populate without waiting for the
            // first aligned boundary (the very first DispatchUpdates uses an immediate
            // fallback for entries whose NextDue still lies in the future).
            DispatchUpdates(initial: true);

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                DispatchUpdates(initial: false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on Stop
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DynamicTextManager tick loop error: {ex.Message}");
        }
    }

    private void DispatchUpdates(bool initial)
    {
        List<Entry> snapshot;
        lock (_gate)
        {
            snapshot = _active;
        }

        var now = DateTime.UtcNow;
        foreach (var entry in snapshot)
        {
            // Initial pass: render once immediately even if the aligned boundary
            // hasn't been reached yet, so the button isn't blank for up to one interval.
            if (!initial && now < entry.NextDueUtc)
                continue;

            string newText;
            try
            {
                newText = entry.Provider.GetText(entry.Parameters) ?? string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DynamicTextManager: provider '{entry.Provider.GetType().Name}' threw: {ex.Message}");
                entry.NextDueUtc = AlignedNext(now, entry.Interval);
                continue;
            }

            var button = entry.Button;
            Dispatcher.UIThread.Post(() => button.Text = newText);

            // Advance NextDue by exactly one interval to stay aligned to the wall clock.
            // If we fell behind by more than one interval, snap forward.
            entry.NextDueUtc += entry.Interval;
            if (entry.NextDueUtc <= now)
                entry.NextDueUtc = AlignedNext(now, entry.Interval);
        }
    }

    private void StopLoop()
    {
        CancellationTokenSource cts;
        PeriodicTimer timer;
        lock (_gate)
        {
            cts = _cts;
            timer = _timer;
            _cts = null;
            _timer = null;
            _active = new List<Entry>();
        }

        try { cts?.Cancel(); } catch { }
        try { timer?.Dispose(); } catch { }
        cts?.Dispose();
    }

    public void Dispose()
    {
        _pageManager.OnTouchPageChanged -= OnTouchPageChanged;
        StopLoop();
    }

    private static string ParseCommandName(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        var end = command.IndexOf('(');
        return end == -1 ? command : command.Substring(0, end);
    }

    private static string[] ParseParameters(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return Array.Empty<string>();

        var start = command.IndexOf('(');
        var end = command.IndexOf(')');
        if (start == -1 || end == -1 || end <= start)
            return Array.Empty<string>();

        var parameterString = command.Substring(start + 1, end - start - 1);
        return parameterString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
