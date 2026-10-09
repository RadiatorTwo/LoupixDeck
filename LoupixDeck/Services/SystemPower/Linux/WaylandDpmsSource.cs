using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace LoupixDeck.Services.SystemPower.Linux;

/// <summary>
/// Wayland compositors other than GNOME: watches every output's power mode through
/// <c>org_kde_kwin_dpms_manager</c> (KWin) or <c>zwlr_output_power_manager_v1</c> (wlroots:
/// sway, Hyprland, ...). The handful of messages involved carry no file descriptors, so the
/// wire protocol is spoken directly on the compositor socket; no libwayland binding is needed.
/// A dedicated thread blocks on the socket and only wakes when the compositor sends something.
/// </summary>
public sealed class WaylandDpmsSource : ILinuxDisplaySource
{
    private const string KdeDpmsManager = "org_kde_kwin_dpms_manager";
    private const string WlrPowerManager = "zwlr_output_power_manager_v1";
    private const string WlOutput = "wl_output";

    private const uint DisplayId = 1;

    // wl_display
    private const ushort DisplaySync = 0;
    private const ushort DisplayGetRegistry = 1;
    private const ushort DisplayErrorEvent = 0;

    // wl_registry
    private const ushort RegistryBind = 0;
    private const ushort RegistryGlobalEvent = 0;
    private const ushort RegistryGlobalRemoveEvent = 1;

    // wl_callback
    private const ushort CallbackDoneEvent = 0;

    // Both managers: get(new_id, wl_output) is request 0.
    private const ushort ManagerGet = 0;

    // org_kde_kwin_dpms events; mode 0 = On, 1 Standby, 2 Suspend, 3 Off.
    private const ushort KdeModeEvent = 1;
    private const ushort KdeDoneEvent = 2;

    // zwlr_output_power_v1 events; mode 0 = off, 1 = on.
    private const ushort WlrModeEvent = 0;
    private const ushort WlrFailedEvent = 1;

    /// <summary>An output being watched, keyed by the power object made for it.</summary>
    private sealed class OutputPower
    {
        public uint GlobalName;
        public bool On = true;
        public bool PendingOn = true;
    }

    private readonly Dictionary<uint, OutputPower> _outputsByPower = new();
    private readonly List<(uint Name, uint Version)> _pendingOutputs = [];
    private Socket _socket;
    private Thread _reader;
    private Action<bool> _report;
    private uint _nextId = 2;
    private uint _registryId;
    private uint _syncId;
    private string _managerInterface;
    private uint _managerId;
    private bool _initialRoundTripDone;
    private bool _lastReported = true;

    public string Name => _managerInterface == null ? "Wayland" : $"Wayland {_managerInterface}";

    public Task<bool> TryStartAsync(Action<bool> report)
    {
        string path = SocketPath();
        if (path == null || !File.Exists(path)) return Task.FromResult(false);

        _report = report;
        _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _socket.Connect(new UnixDomainSocketEndPoint(path));

        _registryId = _nextId++;
        Send(DisplayId, DisplayGetRegistry, w => w.Uint(_registryId));
        _syncId = _nextId++;
        Send(DisplayId, DisplaySync, w => w.Uint(_syncId));

        // The first round trip lists every global; after it we know whether a power protocol exists.
        while (!_initialRoundTripDone)
            Dispatch(ReadMessage());

        if (_managerInterface == null) return Task.FromResult(false);

        foreach ((uint name, uint version) in _pendingOutputs)
            WatchOutput(name, version);
        _pendingOutputs.Clear();
        PublishIfChanged(force: true);

        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "Wayland display state" };
        _reader.Start();
        return Task.FromResult(true);
    }

    private static string SocketPath()
    {
        string display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (string.IsNullOrEmpty(display)) return null;
        if (Path.IsPathRooted(display)) return display;
        string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return string.IsNullOrEmpty(runtimeDir) ? null : Path.Combine(runtimeDir, display);
    }

    private void ReadLoop()
    {
        try
        {
            while (true)
                Dispatch(ReadMessage());
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or EndOfStreamException)
        {
            // Disposed, or the compositor went away. Nothing reports any more.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Displays] Wayland display state reader stopped: {ex.Message}");
        }
    }

    private void Dispatch((uint ObjectId, ushort Opcode, byte[] Payload) message)
    {
        (uint objectId, ushort opcode, byte[] payload) = message;
        WireReader r = new(payload);

        if (objectId == DisplayId)
        {
            if (opcode == DisplayErrorEvent)
                throw new InvalidOperationException(
                    $"Wayland protocol error on object {r.Uint()} code {r.Uint()}: {r.String()}");
            return; // delete_id
        }

        if (objectId == _syncId && opcode == CallbackDoneEvent)
        {
            _initialRoundTripDone = true;
            return;
        }

        if (objectId == _registryId)
        {
            if (opcode == RegistryGlobalEvent)
                OnGlobal(r.Uint(), r.String(), r.Uint());
            else if (opcode == RegistryGlobalRemoveEvent)
                OnGlobalRemoved(r.Uint());
            return;
        }

        if (!_outputsByPower.TryGetValue(objectId, out OutputPower output)) return;

        if (_managerInterface == KdeDpmsManager)
        {
            if (opcode == KdeModeEvent)
                output.PendingOn = r.Uint() == 0;
            else if (opcode == KdeDoneEvent)
            {
                output.On = output.PendingOn;
                PublishIfChanged();
            }
        }
        else if (opcode == WlrModeEvent)
        {
            output.On = r.Uint() != 0;
            PublishIfChanged();
        }
        else if (opcode == WlrFailedEvent)
        {
            // The output is gone or cannot be controlled; it no longer has a say.
            _outputsByPower.Remove(objectId);
            PublishIfChanged();
        }
    }

    private void OnGlobal(uint name, string iface, uint version)
    {
        if (iface == WlOutput)
        {
            if (_managerId == 0) _pendingOutputs.Add((name, version));
            else WatchOutput(name, version);
        }
        else if (_managerInterface == null && iface is KdeDpmsManager or WlrPowerManager)
        {
            _managerInterface = iface;
            _managerId = Bind(name, iface, 1);
        }
    }

    private void OnGlobalRemoved(uint name)
    {
        _pendingOutputs.RemoveAll(o => o.Name == name);
        foreach (KeyValuePair<uint, OutputPower> entry in _outputsByPower.Where(e => e.Value.GlobalName == name).ToList())
            _outputsByPower.Remove(entry.Key);
        PublishIfChanged();
    }

    private void WatchOutput(uint name, uint version)
    {
        // Version 1 is enough: the output object is only needed as the argument of get().
        uint outputId = Bind(name, WlOutput, Math.Min(version, 1u));
        uint powerId = _nextId++;
        Send(_managerId, ManagerGet, w => w.Uint(powerId).Uint(outputId));
        _outputsByPower[powerId] = new OutputPower { GlobalName = name };
    }

    private uint Bind(uint name, string iface, uint version)
    {
        uint id = _nextId++;
        Send(_registryId, RegistryBind, w => w.Uint(name).String(iface).Uint(version).Uint(id));
        return id;
    }

    /// <summary>The displays count as off only when every watched output is off.</summary>
    private void PublishIfChanged(bool force = false)
    {
        bool anyOn = _outputsByPower.Count == 0 || _outputsByPower.Values.Any(o => o.On);
        if (!force && anyOn == _lastReported) return;
        _lastReported = anyOn;
        _report?.Invoke(anyOn);
    }

    private void Send(uint objectId, ushort opcode, Action<WireWriter> args)
    {
        WireWriter w = new();
        args(w);
        byte[] body = w.ToArray();
        byte[] message = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(message, objectId);
        BinaryPrimitives.WriteUInt32LittleEndian(message.AsSpan(4), ((uint)message.Length << 16) | opcode);
        body.CopyTo(message, 8);
        _socket.Send(message);
    }

    private (uint ObjectId, ushort Opcode, byte[] Payload) ReadMessage()
    {
        byte[] header = ReadExactly(8);
        uint objectId = BinaryPrimitives.ReadUInt32LittleEndian(header);
        uint word = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        int size = (int)(word >> 16);
        if (size < 8) throw new InvalidDataException("Malformed Wayland message.");
        return (objectId, (ushort)(word & 0xFFFF), ReadExactly(size - 8));
    }

    private byte[] ReadExactly(int count)
    {
        byte[] buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = _socket.Receive(buffer, read, count - read, SocketFlags.None);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
        return buffer;
    }

    public void Dispose()
    {
        // Closing the socket ends the blocking read and with it the reader thread.
        try { _socket?.Shutdown(SocketShutdown.Both); } catch { /* already closed */ }
        _socket?.Dispose();
        _socket = null;
        _report = null;
    }

    /// <summary>Wayland arguments: 32-bit little-endian words, strings length-prefixed,
    /// NUL-terminated and padded to a word.</summary>
    private sealed class WireWriter
    {
        private readonly MemoryStream _stream = new();

        public WireWriter Uint(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            _stream.Write(bytes);
            return this;
        }

        public WireWriter String(string value)
        {
            byte[] text = Encoding.UTF8.GetBytes(value);
            Uint((uint)text.Length + 1);
            _stream.Write(text);
            int padded = (text.Length + 1 + 3) & ~3;
            for (int i = text.Length; i < padded; i++) _stream.WriteByte(0);
            return this;
        }

        public byte[] ToArray() => _stream.ToArray();
    }

    private ref struct WireReader(byte[] payload)
    {
        private readonly byte[] _payload = payload;
        private int _offset;

        public uint Uint()
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(_offset));
            _offset += 4;
            return value;
        }

        public string String()
        {
            int length = (int)Uint();
            string value = length == 0 ? null : Encoding.UTF8.GetString(_payload, _offset, length - 1);
            _offset += (length + 3) & ~3;
            return value;
        }
    }
}
