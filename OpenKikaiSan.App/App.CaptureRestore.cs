using OpenKikaiSan.App.Models;
using OpenKikaiSan.App.Services.WindowCapture;
using OpenKikaiSan.App.Stores;
using OpenKikaiSan.App.ViewModels;

namespace OpenKikaiSan.App;

public partial class App
{
    private static void SaveCaptureTargetSelection(
        AppSettings settings,
        SettingsStore settingsStore,
        SavedCaptureTarget? savedCaptureTarget
    )
    {
        settings.SavedCaptureTarget = savedCaptureTarget;
        settingsStore.Save(settings);
    }

    private void RestoreCaptureTargetIfAvailable(
        AppSettings settings,
        CaptureTargetRestoreService captureTargetRestoreService,
        MainViewModel mainViewModel
    )
    {
        if (_windowCaptureService is null || settings.SavedCaptureTarget is null)
        {
            return;
        }

        var restoredItem = captureTargetRestoreService.TryRestore(settings.SavedCaptureTarget);
        if (restoredItem is null)
        {
            return;
        }

        if (_windowCaptureService.StartCapture(restoredItem))
        {
            mainViewModel.CaptureStatus = _windowCaptureService.GetStatusText();
            mainViewModel.StatusMessage = "Previous capture target restored.";
        }
    }
}
