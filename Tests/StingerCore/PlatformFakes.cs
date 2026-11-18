using HyperXBatteryTray.Devices;

namespace HyperXBatteryTray.Hid
{
    // Scripted HID boundary; no hardware, registry, or OS device discovery.
    public sealed class HidConnection : IDisposable
    {
        public static Func<int, byte[]?> BatteryReply = _ => BatteryPacket();
        public static Func<string, Stream?> OpenMicrophone = _ => null;
        public static int MicrophoneOpens;
        public static int BatteryReads;
        public static int ConnectionReads;
        public static Func<HyperXDeviceDefinition, bool> Present = _ => true;
        public static readonly List<byte[]> Commands = new();
        private bool _batteryRequest;
        private int _attempt;
        public string? DevicePath { get; private set; }

        public static void Reset()
        {
            BatteryReply = _ => BatteryPacket();
            OpenMicrophone = _ => null;
            MicrophoneOpens = BatteryReads = ConnectionReads = 0;
            Commands.Clear();
            Present = _ => true;
        }

        public static byte[] BatteryPacket(byte charge = 3, short millivolts = 3960)
        {
            byte[] packet = new byte[64];
            packet[0] = 0xFF; packet[1] = 0x12; packet[2] = 0x05;
            packet[9] = charge;
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(11), millivolts);
            return packet;
        }

        public static string FindDevice(HyperXDeviceDefinition definition) => definition.InterfacePattern;
        public bool Open(string path) { DevicePath = path; return true; }
        public void Close() => DevicePath = null;
        public void Dispose() => Close();
        public void SetFeatureReport(byte[] command)
        {
            Commands.Add(command.ToArray());
            _batteryRequest = command[5] == 0x0C;
            _attempt = 0;
        }
        public byte[]? GetFeatureReport(byte id)
        {
            if (id != 0xFF) throw new InvalidOperationException("Unexpected feature ID");
            if (_batteryRequest) { BatteryReads++; return BatteryReply(++_attempt); }
            ConnectionReads++;
            byte[] packet = new byte[64]; packet[11] = 1; return packet;
        }
        public static Stream? OpenSiblingInputStream(string path, string pattern, ushort page, ushort usage, ushort length)
        {
            if (!path.Contains("VID_03F0&PID_0A8E") || pattern != "VID_03F0&PID_0A8E&MI_03&Col02" ||
                page != 0xFFC0 || usage != 1 || length != 2)
                throw new InvalidOperationException("Wrong microphone endpoint");
            Interlocked.Increment(ref MicrophoneOpens);
            return OpenMicrophone(path);
        }
        public sealed record EndpointCandidate(string Path, string Collection);
        public static IReadOnlyList<EndpointCandidate> EnumerateControlEndpoints(HyperXDeviceDefinition definition) =>
            Present(definition) ? new[] { new EndpointCandidate(definition.InterfacePattern, "Col03") } : [];
        public void Write(byte[] command) => throw new InvalidOperationException("Unexpected output report");
        public void WriteConfigurationReport(byte[] command) => throw new InvalidOperationException("Unexpected configuration write");
        public Task<byte[]?> ReadAsync(CancellationToken token) => throw new InvalidOperationException("Unexpected primary input read");
    }

    internal static class HidEndpointPreferenceStore
    {
        public static string? Get(string model, ushort vendor, ushort product) => null;
        public static void Remember(string model, ushort vendor, ushort product, string collection) { }
    }
}

namespace HyperXBatteryTray.Diagnostics
{
    internal static class BetaDiagnostics
    {
        public static void Write(string name, object value) { }
        public static void WriteError(string name, Exception error) { }
    }
}

namespace HyperXBatteryTray
{
    internal static class AppReleaseInfo { public static bool IsBeta => true; }
}

namespace HyperXBatteryTray.Devices
{
    // Referenced by the common monitor but never instantiated by these DTS tests.
    internal sealed class BluetoothFallbackDevice : HyperXBatteryDeviceBase
    {
        public BluetoothFallbackDevice() : base(CloudStingerCoreWirelessDevice.DtsDefinition) { }
        public int TransportGeneration => 0;
    }
}
