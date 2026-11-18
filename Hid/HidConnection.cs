using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using HyperXBatteryTray.Devices;
using HyperXBatteryTray.Diagnostics;

namespace HyperXBatteryTray.Hid;

public sealed class HidConnection : IDisposable
{
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;

    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;

    private const uint OPEN_EXISTING = 3;

    private const int ERROR_NO_MORE_ITEMS = 259;
    // SP_DEVICE_INTERFACE_DETAIL_DATA_W starts with DWORD cbSize;
    // the UTF-16 DevicePath begins immediately after this field (offset 4),
    // regardless of the native structure's padded cbSize on x64.
    private const int DEVICE_INTERFACE_PATH_OFFSET = sizeof(int);

    private static readonly Guid HidClassGuid =
        new("4D1E55B2-F16F-11CF-88CB-001111000030");

    private SafeFileHandle? _writeHandle;
    private SafeFileHandle? _readHandle;

    private FileStream? _writeStream;
    private FileStream? _readStream;

    private bool _disposed;
    // Protect both Win32 output calls and stream writes across monitoring/UI threads.
    private readonly object _outputGate = new();

    private void TraceReport(string direction, string method, string status,
        byte[]? report, int bytes, Exception? error = null)
    {
        if (!BetaDiagnostics.IsEnabled) return;
        BetaDiagnostics.Write("HID.RawReport", new
        {
            Direction = direction,
            Method = method,
            Status = status,
            DevicePath,
            ReportId = bytes > 0 && report != null ? $"{report[0]:X2}" : null,
            ReportLength = bytes,
            Hex = report == null ? null : Convert.ToHexString(report.AsSpan(0, Math.Min(bytes, report.Length))),
            Exception = error?.GetType().Name,
            HResult = error?.HResult,
            Win32Error = error is Win32Exception native ? native.NativeErrorCode : (int?)null
        });
    }


    public string? DevicePath { get; private set; }

    public int InputReportByteLength { get; private set; }

    public int OutputReportByteLength { get; private set; }

    public int FeatureReportByteLength { get; private set; }

    public bool IsOpen =>
        _writeHandle is { IsInvalid: false } &&
        _readHandle is { IsInvalid: false };

    public bool Open(string devicePath)
    {
        ThrowIfDisposed();

        Close();
        if (BetaDiagnostics.IsEnabled)
            BetaDiagnostics.Write("HID.OpenAttempt", new { Path = devicePath });

        _writeHandle = CreateFile(
            devicePath,
            GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (_writeHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();

            _writeHandle.Dispose();
            _writeHandle = null;
            if (BetaDiagnostics.IsEnabled)
                BetaDiagnostics.Write("HID.OpenFailed", new { Path = devicePath, Stage = "WriteHandle", ErrorCode = error });

            throw new Win32Exception(
                error,
                $"Não foi possível abrir o HID para escrita. " +
                $"Erro {error}.");
        }

        _readHandle = CreateFile(
            devicePath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (_readHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();

            if (BetaDiagnostics.IsEnabled)
                BetaDiagnostics.Write("HID.OpenFailed", new { Path = devicePath, Stage = "ReadHandle", ErrorCode = error });
            Close();

            throw new Win32Exception(
                error,
                $"Não foi possível abrir o HID para leitura. " +
                $"Erro {error}.");
        }

        HidCapabilities? capabilities = GetHidCapabilities(_readHandle);

        if (!capabilities.HasValue ||
            capabilities.Value.InputReportByteLength == 0 ||
            capabilities.Value.OutputReportByteLength == 0)
        {
            Close();
            throw new InvalidOperationException(
                "Não foi possível obter os tamanhos dos reports HID do dispositivo.");
        }

        InputReportByteLength = capabilities.Value.InputReportByteLength;
        OutputReportByteLength = capabilities.Value.OutputReportByteLength;
        FeatureReportByteLength = capabilities.Value.FeatureReportByteLength;

        _writeStream = new FileStream(
            _writeHandle,
            FileAccess.Write,
            4096,
            isAsync: false);

        _readStream = new FileStream(
            _readHandle,
            FileAccess.Read,
            4096,
            isAsync: false);

        DevicePath = devicePath;
        if (BetaDiagnostics.IsEnabled)
            BetaDiagnostics.Write("HID.Opened", new { DevicePath, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength });

        return true;
    }

    private byte[] PrepareOutputReport(byte[] report)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(report);
        if (_writeHandle is not { IsInvalid: false, IsClosed: false } || OutputReportByteLength <= 0)
            throw new InvalidOperationException("O dispositivo HID não está aberto.");
        if (report.Length == 0 || report.Length > OutputReportByteLength)
            throw new InvalidOperationException(
                $"O comando HID possui {report.Length} bytes, mas o dispositivo aceita {OutputReportByteLength} bytes.");
        if (report.Length == OutputReportByteLength) return report;
        byte[] padded = new byte[OutputReportByteLength];
        Buffer.BlockCopy(report, 0, padded, 0, report.Length);
        return padded;
    }

    public void Write(byte[] report)
    {
        byte[] outputReport;
        try { outputReport = PrepareOutputReport(report); }
        catch (Exception ex)
        {
            TraceReport("TX", "FileStream.Write", "Rejected", report, report?.Length ?? 0, ex);
            throw;
        }
        lock (_outputGate)
        {
            try
            {
                if (_writeStream == null)
                    throw new InvalidOperationException("O dispositivo HID não está aberto para escrita.");
                _writeStream.Write(outputReport, 0, outputReport.Length);
                _writeStream.Flush();
                TraceReport("TX", "FileStream.Write", "Success", outputReport, outputReport.Length);
            }
            catch (Exception ex)
            {
                TraceReport("TX", "FileStream.Write", "Error", outputReport, outputReport.Length, ex);
                throw;
            }
        }
    }

    /// <summary>
    /// Configuration commands use the native HID output-report API, as supported
    /// by NGENUITY; fall back to the existing stream transport if unsupported.
    /// Never infer successful device persistence from successful USB submission.
    /// </summary>
    public void WriteConfigurationReport(byte[] report)
    {
        byte[] outputReport;
        try { outputReport = PrepareOutputReport(report); }
        catch (Exception ex)
        {
            TraceReport("TX", "HidD_SetOutputReport", "Rejected", report, report?.Length ?? 0, ex);
            throw;
        }
        lock (_outputGate)
        {
            if (HidD_SetOutputReport(_writeHandle!, outputReport, outputReport.Length))
            {
                TraceReport("TX", "HidD_SetOutputReport", "Success", outputReport, outputReport.Length);
                return;
            }
            int error = Marshal.GetLastWin32Error();
            if (BetaDiagnostics.IsEnabled)
                BetaDiagnostics.Write("HID.OutputReportFallback", new
                {
                    DevicePath, Win32Error = error,
                    Method = "HidD_SetOutputReport", Status = "NativeCallFailed",
                    Hex = Convert.ToHexString(outputReport)
                });
            try
            {
                if (_writeStream == null)
                    throw new InvalidOperationException("O dispositivo HID não está aberto para escrita.");
                _writeStream.Write(outputReport, 0, outputReport.Length);
                _writeStream.Flush();
                TraceReport("TX", "FileStream.Write (fallback)", "Success", outputReport, outputReport.Length);
            }
            catch (Exception ex)
            {
                TraceReport("TX", "FileStream.Write (fallback)", "Error", outputReport, outputReport.Length, ex);
                throw;
            }
        }
    }

    public void SetFeatureReport(byte[] report)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(report);
        ValidateFeatureReportHandle();
        if (report.Length == 0 || report.Length > FeatureReportByteLength)
            throw new ArgumentException("Invalid HID feature report size.", nameof(report));
        byte[] buffer = new byte[FeatureReportByteLength];
        Buffer.BlockCopy(report, 0, buffer, 0, report.Length);
        lock (_outputGate)
        {
            try
            {
                if (!HidD_SetFeature(_writeHandle!, buffer, buffer.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                TraceReport("TX", "HidD_SetFeature", "Success", buffer, buffer.Length);
            }
            catch (Exception ex)
            {
                TraceReport("TX", "HidD_SetFeature", "Error", buffer, buffer.Length, ex);
                throw;
            }
        }
    }

    public byte[] GetFeatureReport(byte reportId)
    {
        ThrowIfDisposed();
        ValidateFeatureReportHandle();
        byte[] buffer = new byte[FeatureReportByteLength];
        buffer[0] = reportId;
        lock (_outputGate)
        {
            try
            {
                TraceReport("TX", "HidD_GetFeature.Request", "Started", buffer, 1);
                if (!HidD_GetFeature(_writeHandle!, buffer, buffer.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                TraceReport("RX", "HidD_GetFeature", "Success", buffer, buffer.Length);
                return buffer;
            }
            catch (Exception ex)
            {
                TraceReport("RX", "HidD_GetFeature", "Error", buffer, buffer.Length, ex);
                throw;
            }
        }
    }

    private void ValidateFeatureReportHandle()
    {
        if (_writeHandle is not { IsInvalid: false, IsClosed: false } || FeatureReportByteLength <= 0)
            throw new InvalidOperationException("HID feature reports are unavailable.");
    }

    public async Task<byte[]?> ReadAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (_readStream == null || InputReportByteLength <= 0)
        {
            throw new InvalidOperationException(
                "O dispositivo HID não está aberto.");
        }

        byte[] buffer = new byte[InputReportByteLength];

        try
        {
            int bytesRead =
                await _readStream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken);

            if (bytesRead <= 0)
            {
                TraceReport("RX", "FileStream.ReadAsync", "EndOfStream", null, 0);
                return null;
            }

            TraceReport("RX", "FileStream.ReadAsync", "Success", buffer, bytesRead);
            if (bytesRead == buffer.Length)
                return buffer;

            byte[] result = new byte[bytesRead];

            Buffer.BlockCopy(
                buffer,
                0,
                result,
                0,
                bytesRead);

            return result;
        }
        catch (OperationCanceledException)
        {
            if (BetaDiagnostics.IsEnabled)
                BetaDiagnostics.Write("HID.ReadCanceled", new { DevicePath });
            return null;
        }
        catch (ObjectDisposedException ex)
        {
            TraceReport("RX", "FileStream.ReadAsync", "Disposed", null, 0, ex);
            return null;
        }
        catch (IOException ex)
        {
            TraceReport("RX", "FileStream.ReadAsync", "Error", null, 0, ex);
            if (cancellationToken.IsCancellationRequested)
                return null;
            throw;
        }
        catch (Exception ex)
        {
            TraceReport("RX", "FileStream.ReadAsync", "Error", null, 0, ex);
            throw;
        }
    }

    internal sealed record EndpointCandidate(
        string Path,
        string Collection,
        ushort UsagePage,
        ushort Usage,
        ushort InputLength,
        ushort OutputLength);

    /// <summary>
    /// Enumerates only the documented control collection for a family. Legacy
    /// definitions without an explicit usage are kept on their original first
    /// matching HID path, rather than probing unknown firmware/audio interfaces.
    /// Enumeration uses metadata-only handles and sends no device reports.
    /// </summary>
    internal static IReadOnlyList<EndpointCandidate> EnumerateControlEndpoints(
        HyperXDeviceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // A physically documented exact MI/Col already pins a control endpoint.
        // Preserve its established connection path if descriptor-only access is
        // restricted by an existing driver or another application.
        bool exactCollection = definition.InterfacePattern.Contains("&Col",
            StringComparison.OrdinalIgnoreCase) &&
            definition.AlternateInterfacePatterns.Count == 0;
        if (exactCollection || definition.ControlUsagePage is null ||
            definition.ControlUsage is null)
        {
            string? originalPath = FindDevice(definition);
            return originalPath == null
                ? Array.Empty<EndpointCandidate>()
                : new[] { new EndpointCandidate(originalPath,
                    ExtractCollection(originalPath), 0, 0, 0, 0) };
        }

        List<EndpointCandidate> candidates = new();
        foreach (string path in EnumerateDevicePaths(definition.Matches))
        {
            // Open access 0: descriptor-only inspection, no HID writes/reads.
            using SafeFileHandle metadata = CreateFile(path, 0,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                OPEN_EXISTING, 0, IntPtr.Zero);
            if (metadata.IsInvalid)
                continue;

            HidCapabilities? caps = GetHidCapabilities(metadata);
            if (caps is null ||
                caps.Value.UsagePage != definition.ControlUsagePage.Value ||
                caps.Value.Usage != definition.ControlUsage.Value ||
                caps.Value.InputReportByteLength == 0 ||
                caps.Value.OutputReportByteLength == 0)
                continue;

            candidates.Add(new EndpointCandidate(path, ExtractCollection(path),
                caps.Value.UsagePage, caps.Value.Usage,
                caps.Value.InputReportByteLength, caps.Value.OutputReportByteLength));
        }
        return candidates;
    }

    private static string ExtractCollection(string path)
    {
        var match = System.Text.RegularExpressions.Regex.Match(path,
            @"MI_[0-9A-F]{2}&COL[0-9A-F]{2}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
    }

    public static string? FindDevice(HyperXDeviceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return FindDevicePath(definition.Matches);
    }

    public static string? FindDeviceByInterfacePattern(string interfacePattern)
    {
        if (string.IsNullOrWhiteSpace(interfacePattern))
            return null;

        return FindDevicePath(path =>
            path.Contains(
                interfacePattern,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindDevicePath(Func<string, bool> matches) =>
        EnumerateDevicePaths(matches).FirstOrDefault();

    // Input-only collections cannot use Open(), which requires an output report.
    // Match the PnP parent so a second receiver can never supply this one's events.
    internal static FileStream? OpenSiblingInputStream(string devicePath,
        string interfacePattern, ushort usagePage, ushort usage, ushort reportLength)
    {
        uint? parent = GetHidParent(devicePath);
        if (parent == null) return null;
        var paths = EnumerateDevicePaths(path =>
            path.Contains(interfacePattern, StringComparison.OrdinalIgnoreCase) &&
            GetHidParent(path) == parent);
        if (paths.Count != 1) return null;

        SafeFileHandle handle = CreateFile(paths[0], GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
            0x40000000 /* FILE_FLAG_OVERLAPPED */, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid) return null;
            HidCapabilities? caps = GetHidCapabilities(handle);
            if (caps == null || caps.Value.UsagePage != usagePage ||
                caps.Value.Usage != usage || caps.Value.InputReportByteLength != reportLength)
                return null;
            var stream = new FileStream(handle, FileAccess.Read, reportLength, isAsync: true);
            handle = null!; // Ownership transferred to the stream.
            return stream;
        }
        finally { handle?.Dispose(); }
    }

    private static uint? GetHidParent(string path)
    {
        string[] parts = path.Split('#');
        if (parts.Length != 4 || !parts[0].EndsWith("hid", StringComparison.OrdinalIgnoreCase))
            return null;
        string instanceId = $"HID\\{parts[1]}\\{parts[2]}";
        if (CM_Locate_DevNode(out uint node, instanceId, 0) != 0 ||
            CM_Get_Parent(out uint parent, node, 0) != 0)
            return null;
        return parent;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
    private static extern uint CM_Locate_DevNode(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);

    private static IReadOnlyList<string> EnumerateDevicePaths(Func<string, bool> matches)
    {
        List<string> paths = new();
        Guid hidGuid = HidClassGuid;
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero,
            IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == INVALID_HANDLE_VALUE)
            return paths;

        try
        {
            for (uint index = 0; ; index++)
            {
                SP_DEVICE_INTERFACE_DATA interfaceData = new()
                {
                    cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero,
                    ref hidGuid, index, ref interfaceData))
                {
                    if (Marshal.GetLastWin32Error() != ERROR_NO_MORE_ITEMS)
                        System.Diagnostics.Debug.WriteLine("HID interface enumeration failed.");
                    break;
                }

                string? path = GetDevicePath(set, ref interfaceData);
                if (path != null && matches(path))
                    paths.Add(path);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return paths;
    }

    private static HidCapabilities? GetHidCapabilities(
        SafeFileHandle handle)
    {
        if (handle.IsInvalid)
            return null;

        if (!HidD_GetPreparsedData(
                handle,
                out IntPtr preparsedData) ||
            preparsedData == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            HIDP_CAPS capabilities = new()
            {
                Reserved = new ushort[17]
            };

            int status = HidP_GetCaps(
                preparsedData,
                ref capabilities);

            if (status != HIDP_STATUS_SUCCESS)
                return null;

            return new HidCapabilities(
                capabilities.UsagePage, capabilities.Usage,
                capabilities.InputReportByteLength,
                capabilities.OutputReportByteLength,
                capabilities.FeatureReportByteLength);
        }
        finally
        {
            _ = HidD_FreePreparsedData(preparsedData);
        }
    }

    private readonly record struct HidCapabilities(
        ushort UsagePage,
        ushort Usage,
        ushort InputReportByteLength,
        ushort OutputReportByteLength,
        ushort FeatureReportByteLength);

    private static string? GetDevicePath(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        uint requiredSize = 0;

        SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            ref requiredSize,
            IntPtr.Zero);

        if (requiredSize < DEVICE_INTERFACE_PATH_OFFSET + sizeof(char) ||
            requiredSize > int.MaxValue)
            return null;

        IntPtr detailBuffer =
            Marshal.AllocHGlobal(
                (int)requiredSize);

        try
        {
            // Win32 requires sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W) in cbSize:
            // 8 bytes in x64, 6 bytes in x86. The path offset remains 4 bytes.
            Marshal.WriteInt32(
                detailBuffer,
                0,
                IntPtr.Size == 8 ? 8 : 6);

            bool result =
                SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    detailBuffer,
                    requiredSize,
                    ref requiredSize,
                    IntPtr.Zero);

            if (!result)
                return null;

            string? path =
                Marshal.PtrToStringUni(
                    IntPtr.Add(
                        detailBuffer,
                        DEVICE_INTERFACE_PATH_OFFSET));

            if (string.IsNullOrWhiteSpace(path))
                return null;

            // Normalização defensiva do namespace Win32.
            if (path.StartsWith(
                    @"?\",
                    StringComparison.Ordinal))
            {
                path = @"\" + path;
            }

            if (path.StartsWith(
                    @"\?\",
                    StringComparison.Ordinal))
            {
                path = @"\" + path;
            }

            return path;
        }
        finally
        {
            Marshal.FreeHGlobal(
                detailBuffer);
        }
    }

    public void Close()
    {
        if (DevicePath != null && BetaDiagnostics.IsEnabled)
            BetaDiagnostics.Write("HID.Closed", new { DevicePath });
        _writeStream?.Dispose();
        _writeStream = null;

        _readStream?.Dispose();
        _readStream = null;

        _writeHandle?.Dispose();
        _writeHandle = null;

        _readHandle?.Dispose();
        _readHandle = null;

        DevicePath = null;
        InputReportByteLength = 0;
        OutputReportByteLength = 0;
        FeatureReportByteLength = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        Close();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private static readonly IntPtr
        INVALID_HANDLE_VALUE = new(-1);

    private const int HIDP_STATUS_SUCCESS = 0x00110000;

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetOutputReport(
        SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(
        SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(
        SafeFileHandle hidDeviceObject, [In, Out] byte[] reportBuffer, int reportBufferLength);

    [DllImport(
        "hid.dll",
        SetLastError = true)]
    private static extern bool
        HidD_GetPreparsedData(
            SafeFileHandle HidDeviceObject,
            out IntPtr PreparsedData);

    [DllImport(
        "hid.dll",
        SetLastError = true)]
    private static extern bool
        HidD_FreePreparsedData(
            IntPtr PreparsedData);

    [DllImport(
        "hid.dll",
        SetLastError = false)]
    private static extern int
        HidP_GetCaps(
            IntPtr PreparsedData,
            ref HIDP_CAPS Capabilities);

    [DllImport(
        "setupapi.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    private static extern IntPtr
        SetupDiGetClassDevs(
            ref Guid ClassGuid,
            IntPtr Enumerator,
            IntPtr hwndParent,
            uint Flags);

    [DllImport(
        "setupapi.dll",
        SetLastError = true)]
    private static extern bool
        SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet,
            IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid,
            uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA
                DeviceInterfaceData);

    [DllImport(
        "setupapi.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    private static extern bool
        SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA
                DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            uint DeviceInterfaceDetailDataSize,
            ref uint RequiredSize,
            IntPtr DeviceInfoData);

    [DllImport(
        "setupapi.dll",
        SetLastError = true)]
    private static extern bool
        SetupDiDestroyDeviceInfoList(
            IntPtr DeviceInfoSet);

    [DllImport(
        "kernel32.dll",
        SetLastError = true,
        CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle
        CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }
}
