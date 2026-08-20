using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Moniswitch;

internal sealed record InputSource(byte Code, string Name)
{
    public override string ToString() => Name;
}

internal readonly record struct DisplayPathIdentity(
    string DisplayName,
    int AdapterHighPart,
    uint AdapterLowPart,
    uint TargetId);

internal static class DisplayIdentity
{
    private const string DisplayMarker = "DISPLAY";

    public static int NumberOf(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return int.MaxValue;
        }

        var markerIndex = displayName.LastIndexOf(DisplayMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return int.MaxValue;
        }

        var suffix = displayName.AsSpan(markerIndex + DisplayMarker.Length);
        return int.TryParse(suffix, out var number) && number > 0
            ? number
            : int.MaxValue;
    }

    public static string NumberLabel(string? displayName, int fallbackNumber)
    {
        var number = NumberOf(displayName);
        return (number == int.MaxValue ? fallbackNumber : number).ToString("00");
    }

    public static string NumberLabel(int displayNumber, int fallbackNumber) =>
        (displayNumber == int.MaxValue ? fallbackNumber : displayNumber).ToString("00");

    public static IReadOnlyDictionary<string, int> RankTargets(IEnumerable<DisplayPathIdentity> paths)
    {
        var numbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths
                     .OrderBy(item => item.TargetId)
                     .ThenBy(item => item.AdapterHighPart)
                     .ThenBy(item => item.AdapterLowPart))
        {
            if (!numbers.ContainsKey(path.DisplayName))
            {
                numbers[path.DisplayName] = numbers.Count + 1;
            }
        }

        return numbers;
    }
}

internal sealed record MonitorSnapshot(
    string Id,
    string Name,
    string DisplayName,
    string ProductCode,
    string HardwareId,
    int DisplayNumber,
    Rectangle Bounds,
    IReadOnlyList<InputSource> Inputs,
    byte? CurrentInput,
    bool DdcAvailable);

internal static class InputSourceCatalog
{
    public static string NameOf(byte code) => code switch
    {
        0x01 => "VGA 1",
        0x02 => "VGA 2",
        0x03 => "DVI 1",
        0x04 => "DVI 2",
        0x05 => "Composite 1",
        0x06 => "Composite 2",
        0x07 => "S-Video 1",
        0x08 => "S-Video 2",
        0x09 => "Tuner 1",
        0x0A => "Tuner 2",
        0x0B => "Tuner 3",
        0x0C => "Component 1",
        0x0D => "Component 2",
        0x0E => "Component 3",
        0x0F => "DisplayPort 1",
        0x10 => "DisplayPort 2",
        0x11 => "HDMI 1",
        0x12 => "HDMI 2",
        0x1B => "USB-C",
        _ => $"Input 0x{code:X2}"
    };
}

internal sealed class MonitorService : IDisposable
{
    private const byte InputSourceVcpCode = 0x60;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private PhysicalMonitorSet? _monitorSet;

    public MonitorService(bool refreshImmediately = true)
    {
        if (refreshImmediately)
        {
            Refresh();
        }
    }

    public IReadOnlyList<MonitorSnapshot> Monitors =>
        _monitorSet?.Devices.Select(device => device.Snapshot()).ToArray() ?? [];

    public void Refresh()
    {
        var replacement = PhysicalMonitorSet.OpenAll();
        var previous = Interlocked.Exchange(ref _monitorSet, replacement);
        previous?.Dispose();
    }

    public async Task RefreshAsync()
    {
        await _commandLock.WaitAsync();
        try
        {
            await Task.Run(Refresh);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task SwitchAsync(string monitorId, byte input)
    {
        await _commandLock.WaitAsync();
        try
        {
            await Task.Run(() => SwitchCore(monitorId, input));
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task SwitchManyAsync(IReadOnlyDictionary<string, byte> assignments)
    {
        await _commandLock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                var errors = new List<string>();
                foreach (var assignment in assignments)
                {
                    try
                    {
                        SwitchCore(assignment.Key, assignment.Value);
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception.Message);
                    }
                }

                if (errors.Count > 0)
                {
                    throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
                }
            });
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public bool TryGetQuickToggle(string? preferredMonitorId, out MonitorSnapshot monitor)
    {
        var monitors = Monitors.Where(item => item.DdcAvailable && item.Inputs.Count >= 2).ToArray();
        var primaryBounds = Screen.PrimaryScreen?.Bounds;
        monitor = monitors.FirstOrDefault(item => item.Id == preferredMonitorId)
            ?? monitors.FirstOrDefault(item => primaryBounds.HasValue && item.Bounds.IntersectsWith(primaryBounds.Value))
            ?? monitors.FirstOrDefault()!;
        return monitor is not null;
    }

    private void SwitchCore(string monitorId, byte input)
    {
        var device = _monitorSet?.Devices.FirstOrDefault(item => item.Id == monitorId)
            ?? throw new InvalidOperationException("That monitor is no longer available. Refresh and try again.");

        byte? lastReportedInput = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            if (NativeMethods.SetVCPFeature(device.Handle, InputSourceVcpCode, input))
            {
                Thread.Sleep(350);
                if (!device.TryReadCurrentInput(out var reportedInput))
                {
                    // Some monitors stop answering DDC on an inactive source. A
                    // retained handle still lets us send the eventual return command.
                    device.CurrentInput = input;
                    return;
                }

                device.CurrentInput = reportedInput;
                lastReportedInput = reportedInput;
                if (reportedInput == input)
                {
                    return;
                }
            }

            Thread.Sleep(200);
        }

        var requestedName = InputSourceCatalog.NameOf(input);
        var actual = lastReportedInput.HasValue
            ? InputSourceCatalog.NameOf(lastReportedInput.Value)
            : "an unknown input";
        throw new Win32Exception(
            Marshal.GetLastWin32Error(),
            $"{device.Name} stayed on {actual} instead of switching to {requestedName}.");
    }

    public void Dispose()
    {
        _monitorSet?.Dispose();
        _commandLock.Dispose();
    }
}

internal sealed class PhysicalMonitorSet : IDisposable
{
    private PhysicalMonitorSet()
    {
    }

    public List<MonitorDevice> Devices { get; } = [];

    public static PhysicalMonitorSet OpenAll()
    {
        var result = new PhysicalMonitorSet();
        var windowsDisplayNumbers = NativeMethods.GetWindowsDisplayNumbers();
        NativeMethods.MonitorEnumProc callback = (logicalHandle, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfoEx
            {
                Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
            };

            if (!NativeMethods.GetMonitorInfo(logicalHandle, ref info) ||
                !NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(logicalHandle, out var count))
            {
                return true;
            }

            var physical = new NativeMethods.PhysicalMonitor[count];
            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(logicalHandle, count, physical))
            {
                return true;
            }

            var hardwareId = NativeMethods.GetMonitorHardwareId(info.DeviceName);
            var displayNumber = windowsDisplayNumbers.TryGetValue(info.DeviceName, out var mappedNumber)
                ? mappedNumber
                : DisplayIdentity.NumberOf(info.DeviceName);
            var bounds = Rectangle.FromLTRB(
                info.MonitorLeft,
                info.MonitorTop,
                info.MonitorRight,
                info.MonitorBottom);

            foreach (var item in physical)
            {
                result.Devices.Add(new MonitorDevice(
                    item.Handle,
                    info.DeviceName,
                    item.Description,
                    hardwareId,
                    displayNumber,
                    bounds));
            }

            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            result.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate monitors.");
        }

        GC.KeepAlive(callback);

        // Capability requests can be slow. Separate monitors sit on separate DDC
        // channels, so loading them concurrently keeps startup responsive.
        Parallel.ForEach(result.Devices, device => device.LoadMetadata());
        return result;
    }

    public void Dispose()
    {
        foreach (var device in Devices)
        {
            device.Dispose();
        }

        Devices.Clear();
    }
}

internal sealed class MonitorDevice : IDisposable
{
    private const byte InputSourceVcpCode = 0x60;

    public MonitorDevice(
        IntPtr handle,
        string displayName,
        string description,
        string hardwareId,
        int displayNumber,
        Rectangle bounds)
    {
        Handle = handle;
        DisplayName = displayName;
        Description = description;
        HardwareId = hardwareId;
        DisplayNumber = displayNumber;
        Bounds = bounds;
        ProductCode = MonitorCapabilities.GetProductCode(hardwareId);
        Id = string.IsNullOrWhiteSpace(hardwareId)
            ? $"{displayName}:{description}"
            : hardwareId;
        Name = MonitorCapabilities.IsGenericDescription(description)
            ? ProductCode
            : description;
    }

    public IntPtr Handle { get; }
    public string Id { get; }
    public string Name { get; private set; }
    public string DisplayName { get; }
    public string Description { get; }
    public string ProductCode { get; }
    public string HardwareId { get; }
    public int DisplayNumber { get; }
    public Rectangle Bounds { get; }
    public List<InputSource> Inputs { get; } = [];
    public byte? CurrentInput { get; set; }
    public bool DdcAvailable { get; private set; }

    public void LoadMetadata()
    {
        try
        {
            var capabilities = NativeMethods.TryReadCapabilities(Handle);
            var model = MonitorCapabilities.ExtractModel(capabilities);
            if (!string.IsNullOrWhiteSpace(model))
            {
                Name = model;
            }

            foreach (var code in MonitorCapabilities.ExtractInputCodes(capabilities))
            {
                Inputs.Add(new InputSource(code, InputSourceCatalog.NameOf(code)));
            }
        }
        catch
        {
            // A monitor may implement input switching without exposing a valid
            // MCCS capabilities string. Current-input probing below still works.
        }

        if (TryReadCurrentInput(out var current))
        {
            CurrentInput = current;
            DdcAvailable = true;
            if (Inputs.All(item => item.Code != current))
            {
                Inputs.Add(new InputSource(current, InputSourceCatalog.NameOf(current)));
            }
        }

        Inputs.Sort((left, right) => left.Code.CompareTo(right.Code));
    }

    public bool TryReadCurrentInput(out byte input)
    {
        input = 0;
        if (!NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                Handle,
                InputSourceVcpCode,
                out _,
                out var current,
                out _))
        {
            return false;
        }

        input = checked((byte)current);
        return true;
    }

    public MonitorSnapshot Snapshot() => new(
        Id,
        Name,
        DisplayName,
        ProductCode,
        HardwareId,
        DisplayNumber,
        Bounds,
        Inputs.ToArray(),
        CurrentInput,
        DdcAvailable);

    public void Dispose() => NativeMethods.DestroyPhysicalMonitor(Handle);
}

internal static partial class MonitorCapabilities
{
    [GeneratedRegex(@"DISPLAY#([^#]+)#", RegexOptions.IgnoreCase)]
    private static partial Regex ProductCodePattern();

    [GeneratedRegex(@"\b[0-9A-Fa-f]{2}\b")]
    private static partial Regex BytePattern();

    public static string GetProductCode(string hardwareId)
    {
        var match = ProductCodePattern().Match(hardwareId);
        return match.Success ? match.Groups[1].Value : "Unknown monitor";
    }

    public static bool IsGenericDescription(string description) =>
        string.IsNullOrWhiteSpace(description) ||
        description.Contains("Generic PnP", StringComparison.OrdinalIgnoreCase);

    public static string ExtractModel(string capabilities) =>
        ExtractParenthesized(capabilities, "model").Trim();

    public static IReadOnlyList<byte> ExtractInputCodes(string capabilities)
    {
        var block = ExtractParenthesized(capabilities, "60");
        if (string.IsNullOrWhiteSpace(block))
        {
            return [];
        }

        return BytePattern().Matches(block)
            .Select(match => Convert.ToByte(match.Value, 16))
            .Distinct()
            .ToArray();
    }

    private static string ExtractParenthesized(string value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var start = value.IndexOf($"{key}(", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        start += key.Length + 1;
        var depth = 1;
        for (var index = start; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return value[start..index];
                    }

                    break;
            }
        }

        return string.Empty;
    }
}

internal static class NativeMethods
{
    private const uint GetDeviceInterfaceName = 0x00000001;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint DisplayConfigGetSourceName = 1;
    private const int DisplayConfigModeInfoBytes = 64;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    internal delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr monitorRect, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        internal int Size;
        internal int MonitorLeft;
        internal int MonitorTop;
        internal int MonitorRight;
        internal int MonitorBottom;
        internal int WorkLeft;
        internal int WorkTop;
        internal int WorkRight;
        internal int WorkBottom;
        internal uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PhysicalMonitor
    {
        internal IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Description;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        internal int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceString;

        internal uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        internal uint LowPart;
        internal int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigRational
    {
        internal uint Numerator;
        internal uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathSourceInfo
    {
        internal Luid AdapterId;
        internal uint Id;
        internal uint ModeInfoIndex;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathTargetInfo
    {
        internal Luid AdapterId;
        internal uint Id;
        internal uint ModeInfoIndex;
        internal uint OutputTechnology;
        internal uint Rotation;
        internal uint Scaling;
        internal DisplayConfigRational RefreshRate;
        internal uint ScanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        internal bool TargetAvailable;

        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathInfo
    {
        internal DisplayConfigPathSourceInfo SourceInfo;
        internal DisplayConfigPathTargetInfo TargetInfo;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigDeviceInfoHeader
    {
        internal uint Type;
        internal uint Size;
        internal Luid AdapterId;
        internal uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigSourceDeviceName
    {
        internal DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string ViewGdiDeviceName;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string device,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint pathCount,
        out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        IntPtr modes,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr monitor,
        uint count,
        [Out] PhysicalMonitor[] physicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVCPFeatureAndVCPFeatureReply(
        IntPtr monitor,
        byte vcpCode,
        out uint codeType,
        out uint currentValue,
        out uint maximumValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetVCPFeature(IntPtr monitor, byte vcpCode, uint value);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);

    [DllImport("dxva2.dll", EntryPoint = "CapabilitiesRequestAndCapabilitiesReply", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCapabilitiesString(IntPtr monitor, StringBuilder capabilities, uint length);

    internal static string GetMonitorHardwareId(string displayName)
    {
        for (uint index = 0; index < 8; index++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(displayName, index, ref device, GetDeviceInterfaceName))
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(device.DeviceId))
            {
                return device.DeviceId;
            }
        }

        return string.Empty;
    }

    internal static IReadOnlyDictionary<string, int> GetWindowsDisplayNumbers()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != ErrorSuccess)
            {
                break;
            }

            var paths = new DisplayConfigPathInfo[pathCount];
            var modeBytes = checked(Math.Max(1, (int)modeCount) * DisplayConfigModeInfoBytes);
            var modes = Marshal.AllocHGlobal(modeBytes);
            try
            {
                var result = QueryDisplayConfig(
                    QdcOnlyActivePaths,
                    ref pathCount,
                    paths,
                    ref modeCount,
                    modes,
                    IntPtr.Zero);
                if (result == ErrorInsufficientBuffer)
                {
                    continue;
                }

                if (result != ErrorSuccess)
                {
                    break;
                }

                var activePaths = new List<DisplayPathIdentity>();
                for (var index = 0; index < pathCount; index++)
                {
                    var path = paths[index];
                    var sourceName = new DisplayConfigSourceDeviceName
                    {
                        Header = new DisplayConfigDeviceInfoHeader
                        {
                            Type = DisplayConfigGetSourceName,
                            Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                            AdapterId = path.SourceInfo.AdapterId,
                            Id = path.SourceInfo.Id
                        },
                        ViewGdiDeviceName = string.Empty
                    };
                    if (DisplayConfigGetDeviceInfo(ref sourceName) == ErrorSuccess &&
                        !string.IsNullOrWhiteSpace(sourceName.ViewGdiDeviceName))
                    {
                        activePaths.Add(new DisplayPathIdentity(
                            sourceName.ViewGdiDeviceName,
                            path.TargetInfo.AdapterId.HighPart,
                            path.TargetInfo.AdapterId.LowPart,
                            path.TargetInfo.Id));
                    }
                }

                return DisplayIdentity.RankTargets(activePaths);
            }
            finally
            {
                Marshal.FreeHGlobal(modes);
            }
        }

        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    internal static string TryReadCapabilities(IntPtr monitor)
    {
        if (!GetCapabilitiesStringLength(monitor, out var length) || length is 0 or > 65_536)
        {
            return string.Empty;
        }

        var result = new StringBuilder((int)length);
        return GetCapabilitiesString(monitor, result, length)
            ? result.ToString()
            : string.Empty;
    }
}
