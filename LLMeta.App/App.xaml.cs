using System.Windows;
using LLMeta.App.Models;
using LLMeta.App.Services;
using LLMeta.App.Stores;
using LLMeta.App.Utils;
using LLMeta.App.ViewModels;
using Velopack;
using DispatcherTimer = System.Windows.Threading.DispatcherTimer;

namespace LLMeta.App;

public partial class App : System.Windows.Application
{
    private const int AndroidBridgePort = 39090;

    private OpenXrControllerInputService? _openXrControllerInputService;
    private AndroidInputBridgeTcpServerService? _androidInputBridgeTcpServerService;
    private readonly KeyboardInputEmulatorService _keyboardInputEmulatorService = new();
    private DispatcherTimer? _openXrPollTimer;
    private string? _lastOpenXrStatus;

    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureDirectories();

        var logger = new AppLogger();
        logger.Info("Startup begin.");

        try
        {
            DispatcherUnhandledException += (_, args) =>
            {
                logger.Error("DispatcherUnhandledException", args.Exception);
                System.Windows.MessageBox.Show(args.Exception.Message, "LLMeta Error");
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    logger.Error("UnhandledException", ex);
                }
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                logger.Error("UnobservedTaskException", args.Exception);
                args.SetObserved();
            };

            var settingsStore = new SettingsStore(logger);
            var settings = settingsStore.Load();
            var mainViewModel = new MainViewModel(settings, settingsStore, logger);
            mainViewModel.OpenXrReinitializeRequested += () =>
            {
                var reinitializeState = ReinitializeOpenXr(logger);
                mainViewModel.UpdateOpenXrControllerState(reinitializeState);
                if (reinitializeState.IsInitialized)
                {
                    mainViewModel.StatusMessage =
                        "OpenXR reinitialized. Disable keyboard debug input to use real device.";
                }
                else
                {
                    mainViewModel.StatusMessage = "OpenXR reinitialize failed.";
                }
            };

            _androidInputBridgeTcpServerService = new AndroidInputBridgeTcpServerService(
                logger,
                AndroidBridgePort
            );
            _androidInputBridgeTcpServerService.Start();
            mainViewModel.BridgeStatus =
                _androidInputBridgeTcpServerService.StatusText + " (A-1: Android -> 10.0.2.2)";

            var initializeState = ReinitializeOpenXr(logger);
            mainViewModel.UpdateOpenXrControllerState(initializeState);

            MainWindow = new MainWindow { DataContext = mainViewModel };
            MainWindow.PreviewKeyDown += (_, args) =>
            {
                if (mainViewModel.IsKeyboardDebugMode)
                {
                    _keyboardInputEmulatorService.OnKeyDown(args.Key);
                }
            };
            MainWindow.PreviewKeyUp += (_, args) =>
            {
                if (mainViewModel.IsKeyboardDebugMode)
                {
                    _keyboardInputEmulatorService.OnKeyUp(args.Key);
                }
            };
            MainWindow.Show();

            if (_openXrPollTimer is null)
            {
                _openXrPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                _openXrPollTimer.Tick += (_, _) =>
                {
                    OpenXrControllerState state;
                    if (mainViewModel.IsKeyboardDebugMode)
                    {
                        state = _keyboardInputEmulatorService.BuildState();
                        mainViewModel.ActiveInputSource = "Input source: Keyboard debug";
                    }
                    else if (_openXrControllerInputService is not null)
                    {
                        state = _openXrControllerInputService.Poll();
                        mainViewModel.ActiveInputSource = "Input source: OpenXR";
                    }
                    else
                    {
                        state = _keyboardInputEmulatorService.BuildUnavailableState(
                            "OpenXR is not initialized. Click Reinitialize OpenXR or enable keyboard debug input."
                        );
                        mainViewModel.ActiveInputSource = "Input source: unavailable";
                    }

                    mainViewModel.UpdateOpenXrControllerState(state);
                    if (_androidInputBridgeTcpServerService is not null)
                    {
                        _androidInputBridgeTcpServerService.UpdateLatestState(
                            state,
                            mainViewModel.IsKeyboardDebugMode
                        );
                        mainViewModel.BridgeStatus =
                            _androidInputBridgeTcpServerService.StatusText
                            + " (A-1: Android -> 10.0.2.2)";
                    }

                    if (_lastOpenXrStatus != state.Status)
                    {
                        _lastOpenXrStatus = state.Status;
                        logger.Info($"OpenXR input state: {state.Status}");
                    }
                };
                _openXrPollTimer.Start();
            }

            logger.Info("Startup completed.");
        }
        catch (Exception ex)
        {
            logger.Error("Startup failed.", ex);
            System.Windows.MessageBox.Show(ex.Message, "LLMeta Error");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _openXrPollTimer?.Stop();
        _openXrPollTimer = null;
        _openXrControllerInputService?.Dispose();
        _openXrControllerInputService = null;
        _androidInputBridgeTcpServerService?.Dispose();
        _androidInputBridgeTcpServerService = null;
        base.OnExit(e);
    }

    private OpenXrControllerState ReinitializeOpenXr(AppLogger logger)
    {
        _openXrControllerInputService?.Dispose();
        _openXrControllerInputService = null;

        var openXrControllerInputService = new OpenXrControllerInputService();
        var initializeState = openXrControllerInputService.Initialize();
        logger.Info($"OpenXR input initialize: {initializeState.Status}");

        if (initializeState.IsInitialized)
        {
            _openXrControllerInputService = openXrControllerInputService;
        }
        else
        {
            openXrControllerInputService.Dispose();
        }

        return initializeState;
    }
}
