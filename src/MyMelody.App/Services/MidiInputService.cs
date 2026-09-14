using System.Runtime.InteropServices;
using System.Text;

namespace MyMelody.App.Services;

public sealed record MidiDeviceInfo(int Index, string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class MidiNoteEventArgs(int note, int velocity, int channel, DateTimeOffset occurredAt) : EventArgs
{
    public int Note { get; } = note;
    public int Velocity { get; } = velocity;
    public int Channel { get; } = channel;
    public DateTimeOffset OccurredAt { get; } = occurredAt;
}

public sealed class MidiConnectionEventArgs(bool isConnected, string message) : EventArgs
{
    public bool IsConnected { get; } = isConnected;
    public string Message { get; } = message;
}

/// <summary>Receives input only. It never forwards, synthesizes, or changes Cakewalk MIDI messages.</summary>
public sealed class MidiInputService : IDisposable
{
    private readonly object _gate = new();
    private readonly SynchronizationContext? _context;
    private readonly Native.MidiInCallback _callback;
    private readonly Timer _refreshTimer;
    private nint _handle;
    private string? _selectedId;
    private int _openedIndex = -1;
    private int _generation;
    private volatile bool _disposed;
    private bool _suspended;
    private string _lastStatus = "";

    public event EventHandler<MidiNoteEventArgs>? NoteOn;
    public event EventHandler<MidiConnectionEventArgs>? ConnectionChanged;
    public string? SelectedDeviceId => _selectedId;
    public bool IsConnected => _handle != 0;

    public MidiInputService(SynchronizationContext? context = null)
    {
        _context = context ?? SynchronizationContext.Current;
        _callback = OnNativeMessage; // Keep the unmanaged callback alive until midiInClose returns.
        _refreshTimer = new Timer(_ => RefreshDevices(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public static IReadOnlyList<MidiDeviceInfo> GetDevices()
    {
        var devices = new List<MidiDeviceInfo>();
        var duplicates = new Dictionary<string, int>(StringComparer.Ordinal);
        var count = Native.midiInGetNumDevs();
        for (uint index = 0; index < count; index++)
        {
            if (Native.midiInGetDevCapsW((nuint)index, out var caps, (uint)Marshal.SizeOf<Native.MidiInCaps>()) != 0)
                continue;
            // Enumeration indexes change after USB reconnect. Match persistent device properties instead.
            var key = $"{caps.ManufacturerId:X4}:{caps.ProductId:X4}:{caps.Name}";
            duplicates.TryGetValue(key, out var duplicate);
            duplicates[key] = duplicate + 1;
            var id = $"{key}:{duplicate}";
            devices.Add(new MidiDeviceInfo((int)index, id, duplicate == 0 ? caps.Name : $"{caps.Name} ({duplicate + 1})"));
        }
        return devices;
    }

    public void SelectDevice(string? stableId)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CloseCore();
            _selectedId = stableId;
            _lastStatus = "";
        }
        RefreshDevices();
    }

    /// <summary>May also be called when Windows reports WM_DEVICECHANGE.</summary>
    public void RefreshDevices()
    {
        lock (_gate)
        {
            if (_disposed || _suspended) return;
            try
            {
                if (string.IsNullOrEmpty(_selectedId))
                {
                    Report(false, "MIDI 건반을 선택해 주세요.");
                    return;
                }
                var device = GetDevices().FirstOrDefault(x => x.Id == _selectedId);
                if (device is null)
                {
                    CloseCore();
                    Report(false, "건반 연결이 끊어졌어요. 다시 연결하면 자동으로 감지해요.");
                    return;
                }
                if (_handle != 0 && _openedIndex == device.Index) return;
                CloseCore();
                var result = Native.midiInOpen(out _handle, (uint)device.Index, _callback, 0, 0x00030000);
                if (result != 0)
                {
                    _handle = 0;
                    Report(false, result == 4
                        ? "다른 앱이 건반을 단독 사용 중이에요. 멀티클라이언트 MIDI 드라이버 설정을 확인하거나 수동 타이머를 사용해 주세요."
                        : $"건반을 열 수 없어요: {GetError(result)}");
                    return;
                }
                _openedIndex = device.Index;
                result = Native.midiInStart(_handle);
                if (result != 0)
                {
                    CloseCore();
                    Report(false, $"건반 입력을 시작할 수 없어요: {GetError(result)}");
                    return;
                }
                Report(true, $"{device.Name} · 연결됨");
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
            {
                CloseCore();
                Report(false, $"Windows MIDI 장치를 확인할 수 없어요: {ex.Message}");
            }
        }
    }

    public void Suspend()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _suspended = true;
            CloseCore();
            Report(false, "건반 감지가 일시정지되었어요.");
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _suspended = false;
            _lastStatus = "";
        }
        RefreshDevices();
    }

    public static bool TryParseNoteOn(uint packedMessage, out int note, out int velocity, out int channel)
    {
        var status = packedMessage & 0xff;
        note = (int)((packedMessage >> 8) & 0xff);
        velocity = (int)((packedMessage >> 16) & 0xff);
        channel = (int)(status & 0x0f) + 1;
        return (status & 0xf0) == 0x90 && note < 128 && velocity is > 0 and < 128;
    }

    private void OnNativeMessage(nint handle, uint message, nuint instance, nuint param1, nuint param2)
    {
        // Never invoke UI, lock, or call WinMM from a driver callback (that can deadlock midiInClose).
        if (_disposed || message != 0x3c3 || !TryParseNoteOn((uint)param1, out var note, out var velocity, out var channel)) return;
        var generation = Volatile.Read(ref _generation);
        var args = new MidiNoteEventArgs(note, velocity, channel, DateTimeOffset.UtcNow);
        Post(() =>
        {
            if (!_disposed && generation == Volatile.Read(ref _generation) && handle == _handle)
                NoteOn?.Invoke(this, args);
        });
    }

    private void CloseCore()
    {
        Interlocked.Increment(ref _generation);
        var handle = _handle;
        _handle = 0;
        _openedIndex = -1;
        if (handle == 0) return;
        Native.midiInStop(handle);
        Native.midiInReset(handle);
        Native.midiInClose(handle);
        GC.KeepAlive(_callback);
    }

    private void Report(bool connected, string message)
    {
        if (_lastStatus == message) return;
        _lastStatus = message;
        Post(() => { if (!_disposed) ConnectionChanged?.Invoke(this, new MidiConnectionEventArgs(connected, message)); });
    }

    private void Post(Action action)
    {
        if (_context is not null) _context.Post(_ => action(), null);
        else ThreadPool.QueueUserWorkItem(_ => action());
    }

    private static string GetError(uint code)
    {
        var buffer = new StringBuilder(256);
        return Native.midiInGetErrorTextW(code, buffer, (uint)buffer.Capacity) == 0 ? buffer.ToString() : $"MIDI 오류 {code}";
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _refreshTimer.Dispose();
            CloseCore();
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MidiInCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public uint DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            public uint Support;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void MidiInCallback(nint handle, uint message, nuint instance, nuint param1, nuint param2);
        [DllImport("winmm.dll")] internal static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] internal static extern uint midiInGetDevCapsW(nuint deviceId, out MidiInCaps caps, uint size);
        [DllImport("winmm.dll")] internal static extern uint midiInOpen(out nint handle, uint deviceId, MidiInCallback callback, nuint instance, uint flags);
        [DllImport("winmm.dll")] internal static extern uint midiInStart(nint handle);
        [DllImport("winmm.dll")] internal static extern uint midiInStop(nint handle);
        [DllImport("winmm.dll")] internal static extern uint midiInReset(nint handle);
        [DllImport("winmm.dll")] internal static extern uint midiInClose(nint handle);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] internal static extern uint midiInGetErrorTextW(uint error, StringBuilder text, uint length);
    }
}
