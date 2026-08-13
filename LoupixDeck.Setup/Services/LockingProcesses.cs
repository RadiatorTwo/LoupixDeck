using System.Runtime.InteropServices;

namespace LoupixDeck.Setup.Services;

/// <summary>
/// Asks the Windows Restart Manager which processes hold a given file or directory open. Used to turn a
/// bare "access is denied" into a message that names the program the user has to close — the install dir
/// is typically held by a program a LoupixDeck shell command launched, which inherited the folder as its
/// working directory and is in no way obvious from the failure itself.
/// </summary>
internal static unsafe class LockingProcesses
{
    private const int RmRebootReasonNone = 0;
    private const int CchRmSessionKey = 32;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;

    // FILETIME is kept as two uints on purpose: a single long would align the struct to 8 bytes and
    // pad it to 16, shifting every following field of RM_PROCESS_INFO by 4 bytes.
    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public uint ProcessStartTimeLow;
        public uint ProcessStartTimeHigh;
    }

    // Blittable by construction (fixed buffers + int flags) so the NativeAOT build needs no
    // marshalling stub for it.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        public fixed char AppName[CchRmMaxAppName + 1];
        public fixed char ServiceShortName[CchRmMaxSvcName + 1];
        public int ApplicationType;
        public uint AppStatus;
        public uint TsSessionId;
        public int Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, char* sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint sessionHandle, uint files, string[] fileNames,
        uint applications, void* applicationList, uint services, string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint sessionHandle, out uint procInfoNeeded, ref uint procInfo,
        RmProcessInfo* affectedApps, out uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    /// <summary>
    /// Names the processes holding any of <paramref name="paths"/>, formatted as "Name (PID n)". Returns
    /// an empty list when nothing holds them, when the query fails, or when the Restart Manager is
    /// unavailable — this only enriches an error message and must never throw on its own.
    /// Note that the Restart Manager reports open <em>file</em> handles: a process that merely has a
    /// directory as its current directory is not listed, and neither is a locked file when only its
    /// containing directory is passed. Always pass the files themselves.
    /// </summary>
    public static IReadOnlyList<string> Describe(string[] paths)
    {
        if (paths.Length == 0)
            return [];

        uint session = 0;
        bool started = false;
        try
        {
            char* key = stackalloc char[CchRmSessionKey + 1];
            if (RmStartSession(out session, 0, key) != ErrorSuccess)
                return [];
            started = true;

            if (RmRegisterResources(session, (uint)paths.Length, paths, 0, null, 0, null) != ErrorSuccess)
                return [];

            uint available = 0;
            uint needed = 0;
            int result = RmGetList(session, out needed, ref available, null, out _);
            if (result == ErrorSuccess || needed == 0)
                return [];
            if (result != ErrorMoreData)
                return [];

            RmProcessInfo[] infos = new RmProcessInfo[needed];
            available = needed;
            fixed (RmProcessInfo* buffer = infos)
            {
                if (RmGetList(session, out needed, ref available, buffer, out _) != ErrorSuccess)
                    return [];
            }

            List<string> holders = new();
            HashSet<int> seen = new();
            for (int i = 0; i < available; i++)
            {
                if (!seen.Add(infos[i].Process.ProcessId))
                    continue; // one process holding several files is still one culprit

                int pid = infos[i].Process.ProcessId;
                string name;
                fixed (char* appName = infos[i].AppName)
                {
                    name = new string(appName);
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = TryGetProcessName(pid) ?? "Unknown program";

                holders.Add($"{name} (PID {pid})");
            }

            return holders;
        }
        catch
        {
            // The Restart Manager is best-effort diagnostics; a failure here must not mask the
            // original error we are trying to describe.
            return [];
        }
        finally
        {
            if (started)
            {
                try { RmEndSession(session); }
                catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Formats the processes holding files inside <paramref name="directory"/> as a sentence, or an empty
    /// string when none can be determined. The files are queried individually because the Restart Manager
    /// ignores a directory path.
    /// </summary>
    public static string DescribeSentence(string directory)
    {
        string[] files;
        try
        {
            // Capped: the query is diagnostics on an already-failed operation, not worth walking a
            // pathological tree. The install dir is flat enough that the cap won't bite in practice.
            files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Take(512).ToArray();
        }
        catch
        {
            return string.Empty;
        }

        IReadOnlyList<string> holders = Describe(files);
        if (holders.Count == 0)
            return string.Empty;

        return $"Files in the installation folder are still in use by: {string.Join(", ", holders)}. " +
               "Close that program and run the update again.";
    }

    private static string? TryGetProcessName(int pid)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
