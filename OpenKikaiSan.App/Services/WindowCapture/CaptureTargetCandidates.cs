namespace OpenKikaiSan.App.Services.WindowCapture;

internal readonly record struct WindowCaptureCandidate(
    nint Hwnd,
    string Title,
    string ClassName,
    string ExecutablePath,
    int Width,
    int Height
);

internal readonly record struct MonitorCaptureCandidate(
    nint MonitorHandle,
    string DeviceName,
    int Width,
    int Height
);
