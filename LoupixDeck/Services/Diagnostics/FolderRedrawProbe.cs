using System.Diagnostics;

namespace LoupixDeck.Services.Diagnostics;

/// <summary>
/// Times the phases of a folder redraw (#440) so rendering and sending the grid can be compared.
/// Off by default; <c>System.FolderStressTest</c> switches it on. While on, it prints a summary
/// every few seconds: per phase the sample count, average, 95th percentile and maximum.
/// </summary>
public static class FolderRedrawProbe
{
    private const double ReportIntervalMs = 5000;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, List<double>> Samples = new();
    private static readonly List<string> Order = [];
    private static long _windowStart;
    private static int _events;
    private static int _redraws;

    public static bool Enabled { get; private set; }

    public static void Start()
    {
        lock (Gate)
        {
            Samples.Clear();
            Order.Clear();
            _events = 0;
            _redraws = 0;
            _windowStart = Stopwatch.GetTimestamp();
            Enabled = true;
        }

        Console.WriteLine("[FolderProbe] started");
    }

    public static void Stop()
    {
        if (!Enabled) return;
        Report(force: true);
        Enabled = false;
        Console.WriteLine("[FolderProbe] stopped");
    }

    /// <summary>A provider announced a change (before coalescing).</summary>
    public static void CountEvent()
    {
        if (!Enabled) return;
        Interlocked.Increment(ref _events);
    }

    /// <summary>A redraw actually ran (after coalescing).</summary>
    public static void CountRedraw()
    {
        if (!Enabled) return;
        Interlocked.Increment(ref _redraws);
        Report(force: false);
    }

    public static long Begin() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void End(string phase, long start)
    {
        if (!Enabled || start == 0) return;
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        lock (Gate)
        {
            if (!Samples.TryGetValue(phase, out List<double> list))
            {
                list = [];
                Samples[phase] = list;
                Order.Add(phase);
            }

            list.Add(ms);
        }
    }

    private static void Report(bool force)
    {
        lock (Gate)
        {
            double windowMs = Stopwatch.GetElapsedTime(_windowStart).TotalMilliseconds;
            if (!force && windowMs < ReportIntervalMs) return;
            if (windowMs <= 0) return;

            double seconds = windowMs / 1000.0;
            Console.WriteLine($"[FolderProbe] {seconds:F1}s: events {_events / seconds:F1}/s, " +
                              $"redraws {_redraws / seconds:F1}/s");

            foreach (string phase in Order)
            {
                List<double> list = Samples[phase];
                if (list.Count == 0) continue;
                list.Sort();
                double avg = list.Average();
                double p95 = list[Math.Min(list.Count - 1, (int)Math.Ceiling(list.Count * 0.95) - 1)];
                Console.WriteLine($"[FolderProbe]   {phase,-14} n={list.Count,4}  avg={avg,7:F2} ms  " +
                                  $"p95={p95,7:F2} ms  max={list[^1],7:F2} ms");
                list.Clear();
            }

            _events = 0;
            _redraws = 0;
            _windowStart = Stopwatch.GetTimestamp();
        }
    }
}
