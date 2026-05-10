using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;
using LoupixDeck.Models.Argus;

namespace LoupixDeck.Services.Argus;

public interface IArgusMonitorService
{
    bool IsAvailable { get; }
    IReadOnlyList<ArgusSensor> Sensors { get; }
    event Action SnapshotUpdated;
    void Start();
    void Stop();
}

/// <summary>
/// Reads sensor data from Argus Monitor's shared-memory data API.
///
/// Layout (from argus_monitor_data_api.h, #pragma pack(1)):
///   ArgusMonitorData header  =  240 bytes
///     +  0  Signature                    u32
///     +  4  ArgusMajor/MinorA/MinorB/Extra  4 * u8
///     +  8  ArgusBuild                   u32
///     + 12  Version                      u32
///     + 16  CycleCounter                 u32
///     + 20  OffsetForSensorType[27]      27 * u32
///     +128  SensorCount[27]              27 * u32
///     +236  TotalSensorCount             u32
///   Each ArgusMonitorSensorData         = 212 bytes
///     +  0  SensorType                   u32
///     +  4  Label                        wchar_t[64]   (128 bytes UTF-16)
///     +132  UnitString                   wchar_t[32]   ( 64 bytes UTF-16)
///     +196  Value                        f64
///     +204  DataIndex                    u32
///     +208  SensorIndex                  u32
/// </summary>
public sealed class ArgusMonitorService : IArgusMonitorService, IDisposable
{
    private const string MappingName = "Global\\ARGUSMONITOR_DATA_INTERFACE";
    private const string MutexName = "Global\\ARGUSMONITOR_DATA_INTERFACE_MUTEX";
    private const long MappingSize = 1024 * 1024;

    private const int HeaderSize = 240;
    private const int SensorEntrySize = 212;
    private const int MaxSensorCount = 512;

    private const int OffsetCycleCounter = 16;
    private const int OffsetTotalSensorCount = 236;
    private const int OffsetSensorData = 240;

    // Reconnect cadence when Argus Monitor isn't running.
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);
    // Poll cadence when connected — only a 4-byte read happens when CycleCounter is unchanged.
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(250);

    private MemoryMappedFile _mmf;
    private MemoryMappedViewAccessor _accessor;
    private Mutex _mutex;

    private CancellationTokenSource _cts;
    private Task _pollTask;
    private uint _lastCycleCounter;

    private volatile IReadOnlyList<ArgusSensor> _sensors = Array.Empty<ArgusSensor>();

    public IReadOnlyList<ArgusSensor> Sensors => _sensors;
    public bool IsAvailable => _accessor != null;
    public event Action SnapshotUpdated;

    public void Start()
    {
        if (!OperatingSystem.IsWindows())
            return;
        if (_pollTask != null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _pollTask = Task.Run(() => PollLoop(token), token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _pollTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _pollTask = null;
        _cts?.Dispose();
        _cts = null;
        Close();
    }

    public void Dispose() => Stop();

    private async Task PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!IsAvailable)
            {
                if (!TryOpen())
                {
                    try { await Task.Delay(ReconnectDelay, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }
                _lastCycleCounter = 0;
            }

            try
            {
                if (TrySnapshot(out var snapshot))
                {
                    _sensors = snapshot;
                    SnapshotUpdated?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ArgusMonitorService: snapshot failed, will reconnect ({ex.Message}).");
                Close();
            }

            try { await Task.Delay(PollDelay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private bool TryOpen()
    {
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.Read);
            _accessor = _mmf.CreateViewAccessor(0, MappingSize, MemoryMappedFileAccess.Read);
            // The mutex may not yet exist if Argus is mid-startup; treat that as not-available.
            _mutex = Mutex.OpenExisting(MutexName);
            return true;
        }
        catch
        {
            Close();
            return false;
        }
    }

    private void Close()
    {
        try { _accessor?.Dispose(); } catch { }
        try { _mmf?.Dispose(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _accessor = null;
        _mmf = null;
        _mutex = null;
    }

    private unsafe bool TrySnapshot(out IReadOnlyList<ArgusSensor> sensors)
    {
        sensors = null;

        bool acquired = false;
        try
        {
            acquired = _mutex.WaitOne(TimeSpan.FromMilliseconds(500), false);
            if (!acquired)
                return false;

            byte* basePtr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            try
            {
                if (basePtr == null)
                    return false;

                var view = new ReadOnlySpan<byte>(basePtr, (int)MappingSize);

                var cycleCounter = BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetCycleCounter, 4));
                if (cycleCounter == _lastCycleCounter)
                    return false;

                var totalSensorCount = BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetTotalSensorCount, 4));
                if (totalSensorCount > MaxSensorCount)
                    totalSensorCount = MaxSensorCount;

                var list = new List<ArgusSensor>((int)totalSensorCount);
                for (var i = 0u; i < totalSensorCount; i++)
                {
                    var entry = view.Slice(OffsetSensorData + (int)i * SensorEntrySize, SensorEntrySize);

                    var type = (ArgusSensorType)BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(0, 4));
                    var label = ReadWideString(entry.Slice(4, 128));
                    var unit = ReadWideString(entry.Slice(132, 64));
                    var value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(entry.Slice(196, 8)));
                    var dataIndex = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(204, 4));
                    var sensorIndex = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(208, 4));

                    list.Add(new ArgusSensor(type, label, unit, value, dataIndex, sensorIndex));
                }

                _lastCycleCounter = cycleCounter;
                sensors = list;
                return true;
            }
            finally
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died while holding the mutex; the new state is now ours.
            return false;
        }
        finally
        {
            if (acquired)
            {
                try { _mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    private static string ReadWideString(ReadOnlySpan<byte> bytes)
    {
        // UTF-16 little-endian, NUL-terminated. Find the first 0x0000 char.
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
                return Encoding.Unicode.GetString(bytes.Slice(0, i));
        }
        return Encoding.Unicode.GetString(bytes);
    }
}
