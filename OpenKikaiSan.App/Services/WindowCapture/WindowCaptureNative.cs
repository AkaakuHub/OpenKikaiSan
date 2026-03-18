using System.Runtime.InteropServices;
using System.Text;

namespace OpenKikaiSan.App.Services.WindowCapture;

internal static class WindowCaptureNative
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint GwOwner = 4;

    public static IReadOnlyList<WindowCaptureCandidate> EnumerateWindows()
    {
        var windows = new List<WindowCaptureCandidate>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            EnumWindows(
                static (hwnd, lParam) =>
                {
                    var candidatesHandle = GCHandle.FromIntPtr(lParam);
                    var candidates = (List<WindowCaptureCandidate>)candidatesHandle.Target!;
                    if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != IntPtr.Zero)
                    {
                        return true;
                    }

                    var title = GetWindowTitle(hwnd);
                    var className = GetWindowClassName(hwnd);
                    var executablePath = TryGetProcessImagePath(hwnd);
                    if (
                        string.IsNullOrWhiteSpace(className)
                        || string.IsNullOrWhiteSpace(executablePath)
                    )
                    {
                        return true;
                    }

                    if (!TryGetWindowSize(hwnd, out var width, out var height))
                    {
                        return true;
                    }

                    candidates.Add(
                        new WindowCaptureCandidate(
                            hwnd,
                            title,
                            className,
                            executablePath,
                            width,
                            height
                        )
                    );
                    return true;
                },
                GCHandle.ToIntPtr(handle)
            );
        }
        finally
        {
            handle.Free();
        }

        return windows;
    }

    public static IReadOnlyList<MonitorCaptureCandidate> EnumerateMonitors()
    {
        var monitors = new List<MonitorCaptureCandidate>();
        var handle = GCHandle.Alloc(monitors);
        try
        {
            EnumDisplayMonitors(
                IntPtr.Zero,
                IntPtr.Zero,
                static (nint monitorHandle, nint _, ref Rect rect, nint lParam) =>
                {
                    var candidatesHandle = GCHandle.FromIntPtr(lParam);
                    var candidates = (List<MonitorCaptureCandidate>)candidatesHandle.Target!;
                    var monitorInfo = new MonitorInfoEx();
                    monitorInfo.CbSize = Marshal.SizeOf<MonitorInfoEx>();
                    if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
                    {
                        return true;
                    }

                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    candidates.Add(
                        new MonitorCaptureCandidate(
                            monitorHandle,
                            monitorInfo.DeviceName,
                            width,
                            height
                        )
                    );
                    return true;
                },
                GCHandle.ToIntPtr(handle)
            );
        }
        finally
        {
            handle.Free();
        }

        return monitors;
    }

    private static string GetWindowTitle(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetWindowClassName(nint hwnd)
    {
        var builder = new StringBuilder(256);
        var length = GetClassName(hwnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString() : string.Empty;
    }

    private static string TryGetProcessImagePath(nint hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return string.Empty;
        }

        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var builder = new StringBuilder(1024);
            var size = (uint)builder.Capacity;
            return QueryFullProcessImageName(processHandle, 0, builder, ref size)
                ? builder.ToString()
                : string.Empty;
        }
        finally
        {
            _ = CloseHandle(processHandle);
        }
    }

    private static bool TryGetWindowSize(nint hwnd, out int width, out int height)
    {
        if (!GetWindowRect(hwnd, out var rect))
        {
            width = 0;
            height = 0;
            return false;
        }

        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return width > 0 && height > 0;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        nint hdc,
        nint lprcClip,
        MonitorEnumProc lpfnEnum,
        nint dwData
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId
    );

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        nint hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    private delegate bool MonitorEnumProc(
        nint hMonitor,
        nint hdc,
        ref Rect lprcMonitor,
        nint dwData
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int CbSize;
        public Rect RcMonitor;
        public Rect RcWork;
        public uint DwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}
