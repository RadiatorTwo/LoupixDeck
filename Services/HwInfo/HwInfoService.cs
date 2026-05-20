using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;
using LoupixDeck.Models.HwInfo;

namespace LoupixDeck.Services.HwInfo;

public interface IHwInfoService
{
    bool IsAvailable { get; }
    IReadOnlyList<HwInfoSensor> Sensors { get; }
    /// <summary>Last status / error line — surfaced in the HWiNFO settings page.</summary>
    string Diagnostics { get; }
    event Action SnapshotUpdated;
    void Start();
    void Stop();
}

/// <summary>
/// Reads sensor data from HWiNFO's shared-memory interface (HWiNFO Shared Memory Support
/// must be enabled in HWiNFO's settings).
///
/// Layout (from the HWiNFO SDK header, default struct packing):
///   HWiNFO_SENSORS_SHARED_MEM2 header  = 44 bytes
///     +  0  dwSignature                  u32   ('SiWH' when valid)
///     +  4  dwVersion                    u32
///     +  8  dwRevision                   u32
///     + 12  poll_time                    i64   (__time64_t — bumps every HWiNFO poll)
///     + 20  dwOffsetOfSensorSection      u32
///     + 24  dwSizeOfSensorElement        u32
///     + 28  dwNumSensorElements          u32
///     + 32  dwOffsetOfReadingSection     u32
///     + 36  dwSizeOfReadingElement       u32
///     + 40  dwNumReadingElements         u32
///   HWiNFO_SENSORS_SENSOR_ELEMENT      (dwSizeOfSensorElement bytes, 264 or larger)
///     +  0  dwSensorID                   u32
///     +  4  dwSensorInst                 u32
///     +  8  szSensorNameOrig             char[128]
///     +136  szSensorNameUser             char[128]
///     +264  (newer builds append a UTF-8 copy of the user name)
///   HWiNFO_SENSORS_READING_ELEMENT     (dwSizeOfReadingElement bytes, 316 or larger)
///     +  0  tReading                     u32   (SENSOR_READING_TYPE)
///     +  4  dwSensorIndex                u32   (index into the sensor section)
///     +  8  dwReadingID                  u32
///     + 12  szLabelOrig                  char[128]
///     +140  szLabelUser                  char[128]   (system code page)
///     +268  szUnit                       char[16]
///     +284  Value / ValueMin / ValueMax / ValueAvg  4 * f64
///     +316  (newer builds append UTF-8 copies of szLabelUser / szUnit)
///
/// The element is packed (no alignment padding), so the f64 block sits at the fixed
/// offset 284. Newer HWiNFO builds grow the element by appending UTF-8 string copies
/// *after* the values — hence reading from a fixed offset, not <c>size - 32</c>.
/// </summary>
public sealed class HwInfoService : IHwInfoService, IDisposable
{
    private const string MappingName = "Global\\HWiNFO_SENS_SM2";

    // HWiNFO's SDK checks dwSignature == 'SiWH'. That C multi-char literal is the DWORD
    // 0x53695748 (the bytes lie in memory as 'H','W','i','S').
    private const uint ValidSignature = 0x53695748;

    private const int HeaderSize = 44;
    private const int OffsetPollTime = 12;
    private const int OffsetSensorSection = 20;
    private const int OffsetSensorElementSize = 24;
    private const int OffsetSensorCount = 28;
    private const int OffsetReadingSection = 32;
    private const int OffsetReadingElementSize = 36;
    private const int OffsetReadingCount = 40;

    // Offsets inside a sensor element.
    private const int SensorNameUserOffset = 136;
    private const int SensorNameLen = 128;

    // Offsets inside a reading element (the trailing doubles are derived from element size).
    private const int ReadingSensorIndexOffset = 4;
    private const int ReadingIdOffset = 8;
    private const int ReadingLabelUserOffset = 140;
    private const int ReadingLabelLen = 128;
    private const int ReadingUnitOffset = 268;
    private const int ReadingUnitLen = 16;
    // The four f64 values sit immediately after szUnit (the element is packed, no alignment
    // padding). Newer HWiNFO builds append UTF-8 string copies *after* the values, so the
    // offset is fixed here rather than derived from the (variable) element size.
    private const int ReadingValueOffset = ReadingUnitOffset + ReadingUnitLen; // 284

    private const int MaxElements = 4096;

    // Reconnect cadence when HWiNFO (or its shared memory) isn't running.
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(2);
    // Poll cadence when connected — only an 8-byte read happens while poll_time is unchanged.
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(250);

    private MemoryMappedFile _mmf;
    private MemoryMappedViewAccessor _accessor;

    private CancellationTokenSource _cts;
    private Task _pollTask;
    private long _lastPollTime;

    private volatile IReadOnlyList<HwInfoSensor> _sensors = Array.Empty<HwInfoSensor>();
    private volatile string _diagnostics = "Not started";

    public IReadOnlyList<HwInfoSensor> Sensors => _sensors;
    public bool IsAvailable => _accessor != null;
    public string Diagnostics => _diagnostics;
    public event Action SnapshotUpdated;

    private void SetDiagnostics(string text)
    {
        if (_diagnostics == text)
            return;
        _diagnostics = text;
        SnapshotUpdated?.Invoke();
    }

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
                    SetDiagnostics("Shared memory not found — enable 'Shared Memory Support' in HWiNFO.");
                    try { await Task.Delay(ReconnectDelay, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    continue;
                }
                _lastPollTime = 0;
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
                SetDiagnostics($"Snapshot failed ({ex.GetType().Name}: {ex.Message}).");
                Console.WriteLine($"HwInfoService: snapshot failed, will reconnect ({ex}).");
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
            // Length 0 maps the entire shared-memory region.
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
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
        _accessor = null;
        _mmf = null;
    }

    private unsafe bool TrySnapshot(out IReadOnlyList<HwInfoSensor> sensors)
    {
        sensors = null;

        byte* basePtr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
        try
        {
            if (basePtr == null)
                return false;

            var capacity = (int)_accessor.SafeMemoryMappedViewHandle.ByteLength;
            if (capacity < HeaderSize)
                return false;

            var view = new ReadOnlySpan<byte>(basePtr, capacity);

            var signature = BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(0, 4));
            if (signature != ValidSignature)
            {
                SetDiagnostics($"Bad signature 0x{signature:X8} (expected 0x{ValidSignature:X8}).");
                return false;
            }

            var pollTime = BinaryPrimitives.ReadInt64LittleEndian(view.Slice(OffsetPollTime, 8));
            if (pollTime == _lastPollTime)
                return false;

            var sensorOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetSensorSection, 4));
            var sensorElemSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetSensorElementSize, 4));
            var sensorCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetSensorCount, 4));
            var readingOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetReadingSection, 4));
            var readingElemSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetReadingElementSize, 4));
            var readingCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetReadingCount, 4));

            var headerInfo = $"cap={capacity} sOff={sensorOffset} sSize={sensorElemSize} sCnt={sensorCount} " +
                             $"rOff={readingOffset} rSize={readingElemSize} rCnt={readingCount}";

            if (sensorElemSize < SensorNameUserOffset + SensorNameLen ||
                readingElemSize < ReadingUnitOffset + ReadingUnitLen + 32)
            {
                SetDiagnostics($"Unexpected element size — {headerInfo}");
                return false;
            }
            if (sensorCount is < 0 or > MaxElements || readingCount is < 0 or > MaxElements)
            {
                SetDiagnostics($"Element count out of range — {headerInfo}");
                return false;
            }
            if (sensorOffset + (long)sensorCount * sensorElemSize > capacity ||
                readingOffset + (long)readingCount * readingElemSize > capacity)
            {
                SetDiagnostics($"Section exceeds mapping — {headerInfo}");
                return false;
            }

            // Parent sensor section: (id, instance, user name) per group.
            var sensorGroups = new (uint Id, uint Instance, string Name)[sensorCount];
            for (var i = 0; i < sensorCount; i++)
            {
                var elem = view.Slice(sensorOffset + i * sensorElemSize, sensorElemSize);
                sensorGroups[i] = (
                    BinaryPrimitives.ReadUInt32LittleEndian(elem.Slice(0, 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(elem.Slice(4, 4)),
                    ReadAnsiString(elem.Slice(SensorNameUserOffset, SensorNameLen)));
            }

            var list = new List<HwInfoSensor>(readingCount);
            for (var i = 0; i < readingCount; i++)
            {
                var elem = view.Slice(readingOffset + i * readingElemSize, readingElemSize);

                var type = (HwInfoReadingType)BinaryPrimitives.ReadUInt32LittleEndian(elem.Slice(0, 4));
                var sensorIndex = (int)BinaryPrimitives.ReadUInt32LittleEndian(elem.Slice(ReadingSensorIndexOffset, 4));
                var readingId = BinaryPrimitives.ReadUInt32LittleEndian(elem.Slice(ReadingIdOffset, 4));
                var label = ReadAnsiString(elem.Slice(ReadingLabelUserOffset, ReadingLabelLen));
                var unit = ReadAnsiString(elem.Slice(ReadingUnitOffset, ReadingUnitLen));
                var value = BinaryPrimitives.ReadDoubleLittleEndian(elem.Slice(ReadingValueOffset, 8));
                var valueMin = BinaryPrimitives.ReadDoubleLittleEndian(elem.Slice(ReadingValueOffset + 8, 8));
                var valueMax = BinaryPrimitives.ReadDoubleLittleEndian(elem.Slice(ReadingValueOffset + 16, 8));
                var valueAvg = BinaryPrimitives.ReadDoubleLittleEndian(elem.Slice(ReadingValueOffset + 24, 8));

                var group = sensorIndex >= 0 && sensorIndex < sensorCount
                    ? sensorGroups[sensorIndex]
                    : (Id: 0u, Instance: 0u, Name: string.Empty);

                list.Add(new HwInfoSensor(
                    type, group.Name, label, unit,
                    value, valueMin, valueMax, valueAvg,
                    group.Id, group.Instance, readingId));
            }

            _lastPollTime = pollTime;
            sensors = list;
            SetDiagnostics($"OK — {list.Count} readings, {sensorCount} sensors ({headerInfo}).");
            return true;
        }
        finally
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    private static string ReadAnsiString(ReadOnlySpan<byte> bytes)
    {
        // NUL-terminated single-byte string. Latin1 covers HWiNFO's degree sign etc.
        var end = bytes.IndexOf((byte)0);
        if (end < 0)
            end = bytes.Length;
        return Encoding.Latin1.GetString(bytes.Slice(0, end));
    }
}
