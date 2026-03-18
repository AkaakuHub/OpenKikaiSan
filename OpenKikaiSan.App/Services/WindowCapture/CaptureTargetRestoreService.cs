using OpenKikaiSan.App.Models;
using OpenKikaiSan.App.Utils;
using Windows.Graphics.Capture;

namespace OpenKikaiSan.App.Services.WindowCapture;

internal sealed class CaptureTargetRestoreService
{
    private readonly AppLogger _logger;
    private readonly GraphicsCaptureItemFactory _graphicsCaptureItemFactory = new();

    public CaptureTargetRestoreService(AppLogger logger)
    {
        _logger = logger;
    }

    public SavedCaptureTarget? TryCreateSavedTarget(GraphicsCaptureItem item)
    {
        var size = item.Size;
        var monitorCandidate = WindowCaptureNative
            .EnumerateMonitors()
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.DeviceName,
                    item.DisplayName,
                    StringComparison.OrdinalIgnoreCase
                )
                && candidate.Width == size.Width
                && candidate.Height == size.Height
            );
        if (monitorCandidate.MonitorHandle != IntPtr.Zero)
        {
            return new SavedCaptureTarget
            {
                Kind = CaptureTargetKind.Monitor,
                DisplayName = item.DisplayName,
                Width = size.Width,
                Height = size.Height,
                MonitorDeviceName = monitorCandidate.DeviceName,
            };
        }

        var windowCandidate = WindowCaptureNative
            .EnumerateWindows()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Title, item.DisplayName, StringComparison.Ordinal)
                && candidate.Width == size.Width
                && candidate.Height == size.Height
            );
        if (windowCandidate.Hwnd == IntPtr.Zero)
        {
            return null;
        }

        return new SavedCaptureTarget
        {
            Kind = CaptureTargetKind.Window,
            DisplayName = item.DisplayName,
            Width = size.Width,
            Height = size.Height,
            WindowTitle = windowCandidate.Title,
            WindowClassName = windowCandidate.ClassName,
            WindowExecutablePath = windowCandidate.ExecutablePath,
        };
    }

    public GraphicsCaptureItem? TryRestore(SavedCaptureTarget target)
    {
        try
        {
            return target.Kind switch
            {
                CaptureTargetKind.Monitor => TryRestoreMonitor(target),
                CaptureTargetKind.Window => TryRestoreWindow(target),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Capture target restore failed.", ex);
            return null;
        }
    }

    private GraphicsCaptureItem? TryRestoreMonitor(SavedCaptureTarget target)
    {
        var monitorCandidate = WindowCaptureNative
            .EnumerateMonitors()
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.DeviceName,
                    target.MonitorDeviceName,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        if (monitorCandidate.MonitorHandle == IntPtr.Zero)
        {
            return null;
        }

        return _graphicsCaptureItemFactory.CreateForMonitor(monitorCandidate.MonitorHandle);
    }

    private GraphicsCaptureItem? TryRestoreWindow(SavedCaptureTarget target)
    {
        var candidates = WindowCaptureNative
            .EnumerateWindows()
            .Where(candidate =>
                string.Equals(
                    candidate.ExecutablePath,
                    target.WindowExecutablePath,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals(
                    candidate.ClassName,
                    target.WindowClassName,
                    StringComparison.Ordinal
                )
            )
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = CalculateWindowScore(target, candidate),
            })
            .Where(result => result.Score >= 0)
            .OrderByDescending(result => result.Score)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var best = candidates[0].Candidate;
        return _graphicsCaptureItemFactory.CreateForWindow(best.Hwnd);
    }

    private static int CalculateWindowScore(
        SavedCaptureTarget target,
        WindowCaptureCandidate candidate
    )
    {
        if (string.IsNullOrEmpty(target.WindowTitle))
        {
            return 0;
        }

        if (string.Equals(candidate.Title, target.WindowTitle, StringComparison.Ordinal))
        {
            return 1000;
        }

        var commonPrefixLength = 0;
        var compareLength = Math.Min(candidate.Title.Length, target.WindowTitle.Length);
        while (
            commonPrefixLength < compareLength
            && candidate.Title[commonPrefixLength] == target.WindowTitle[commonPrefixLength]
        )
        {
            commonPrefixLength++;
        }

        if (commonPrefixLength > 0)
        {
            return commonPrefixLength;
        }

        return
            candidate.Title.Contains(target.WindowTitle, StringComparison.Ordinal)
            || target.WindowTitle.Contains(candidate.Title, StringComparison.Ordinal)
            ? 1
            : -1;
    }
}
