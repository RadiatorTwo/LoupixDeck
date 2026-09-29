using System.Runtime;

namespace LoupixDeck.Utils;

/// <summary>
/// Hands memory back to the operating system after a short-lived, allocation-heavy window closes.
/// </summary>
/// <remarks>
/// The GC keeps freed memory committed for reuse, so a normal collection leaves the process at its
/// peak size even though nothing uses the memory any more. For an app that mostly idles in the
/// background that peak is what the user sees. An aggressive, compacting collection decommits it.
/// It blocks briefly, so only call it after a window that allocated a lot (e.g. the symbol picker
/// with an icon pack) has closed, never on a hot path.
/// </remarks>
public static class MemoryTrim
{
    public static void ReturnFreedMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
