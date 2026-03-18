namespace OpenKikaiSan.App.Models;

public sealed class SavedCaptureTarget
{
    public CaptureTargetKind Kind { get; set; } = CaptureTargetKind.Window;
    public string DisplayName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string WindowTitle { get; set; } = string.Empty;
    public string WindowClassName { get; set; } = string.Empty;
    public string WindowExecutablePath { get; set; } = string.Empty;
    public string MonitorDeviceName { get; set; } = string.Empty;
}
