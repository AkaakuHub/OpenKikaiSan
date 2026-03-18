using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.UI;

namespace OpenKikaiSan.App.Services.WindowCapture;

internal sealed class GraphicsCaptureItemFactory
{
    public GraphicsCaptureItem? CreateForWindow(nint hwnd)
    {
        var windowId = new WindowId((ulong)hwnd);
        return GraphicsCaptureItem.TryCreateFromWindowId(windowId);
    }

    public GraphicsCaptureItem? CreateForMonitor(nint monitorHandle)
    {
        var displayId = new DisplayId((ulong)monitorHandle);
        return GraphicsCaptureItem.TryCreateFromDisplayId(displayId);
    }
}
