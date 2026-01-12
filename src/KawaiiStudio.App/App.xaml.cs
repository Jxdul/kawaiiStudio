using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KawaiiStudio.App.Services;
using KawaiiStudio.App.ViewModels;

namespace KawaiiStudio.App;

public partial class App : Application
{
    private static readonly TimeSpan DefaultInactivityTimeout = TimeSpan.FromSeconds(45);
    private DispatcherTimer? _inactivityTimer;
    private SettingsService? _settings;

    public static SessionService? Session { get; private set; }
    public static NavigationService? Navigation { get; private set; }
    public static SettingsService? Settings { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appPaths = AppPaths.Resolve();
        var frameCatalog = new FrameCatalogService(appPaths.FramesRoot);
        var templateCatalog = new TemplateCatalogService(appPaths.TemplatesPath);
        var templateStorage = new TemplateStorageService(appPaths.TemplatesPath);
        var frameOverrides = new FrameOverrideService();
        var themeCatalog = new ThemeCatalogService(appPaths.ThemeRoot);
        var session = new SessionService(appPaths);
        session.PruneOldLogs(7);
        var settings = new SettingsService(appPaths);
        _settings = settings;
        Settings = settings;
        Session = session;
        var cameraProvider = CreateCameraProvider(settings);
        var cameraService = new CameraService(cameraProvider);
        var cashAcceptor = new SimulatedCashAcceptorProvider();
        var qrCodes = new QrCodeService();
        var frameComposer = new FrameCompositionService(templateCatalog, qrCodes, frameOverrides);

        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(OnAnyButtonClicked));
        InputManager.Current.PreProcessInput += OnPreProcessInput;
        StartInactivityTimer();

        var navigation = new NavigationService(session);
        Navigation = navigation;
        navigation.Navigated += _ => ResetInactivityTimer();
        var errorViewModel = new ErrorViewModel();
        var startupViewModel = new StartupViewModel(navigation, settings, cameraService, errorViewModel, themeCatalog);
        var homeViewModel = new HomeViewModel(navigation, session, themeCatalog);
        var sizeViewModel = new SizeViewModel(navigation, session, themeCatalog);
        var quantityViewModel = new QuantityViewModel(navigation, session, themeCatalog, settings);
        var layoutViewModel = new LayoutViewModel(navigation, session, themeCatalog);
        var categoryViewModel = new CategoryViewModel(navigation, session, frameCatalog, themeCatalog);
        var frameViewModel = new FrameViewModel(navigation, session, themeCatalog);
        var paymentViewModel = new PaymentViewModel(navigation, session, themeCatalog, settings, cashAcceptor);
        var captureViewModel = new CaptureViewModel(navigation, session, cameraService, themeCatalog);
        var reviewViewModel = new ReviewViewModel(navigation, session, frameComposer, themeCatalog);
        var finalizeViewModel = new FinalizeViewModel(navigation, session, frameComposer, themeCatalog);
        var printingViewModel = new PrintingViewModel(navigation, session, themeCatalog);
        var thankYouViewModel = new ThankYouViewModel(navigation, session, themeCatalog);
        var libraryViewModel = new LibraryViewModel(navigation, frameCatalog, themeCatalog, appPaths);
        var staffViewModel = new StaffViewModel(navigation, themeCatalog, settings);
        var templateEditorViewModel = new TemplateEditorViewModel(navigation, templateStorage, templateCatalog, frameCatalog, frameOverrides, themeCatalog);

        navigation.Register("error", errorViewModel);
        navigation.Register("startup", startupViewModel);
        navigation.Register("home", homeViewModel);
        navigation.Register("size", sizeViewModel);
        navigation.Register("quantity", quantityViewModel);
        navigation.Register("layout", layoutViewModel);
        navigation.Register("category", categoryViewModel);
        navigation.Register("frame", frameViewModel);
        navigation.Register("payment", paymentViewModel);
        navigation.Register("capture", captureViewModel);
        navigation.Register("review", reviewViewModel);
        navigation.Register("finalize", finalizeViewModel);
        navigation.Register("printing", printingViewModel);
        navigation.Register("thank_you", thankYouViewModel);
        navigation.Register("library", libraryViewModel);
        navigation.Register("staff", staffViewModel);
        navigation.Register("template_editor", templateEditorViewModel);

        var mainViewModel = new MainViewModel(navigation);
        var window = new MainWindow { DataContext = mainViewModel };

        navigation.Navigate("startup");
        window.Show();
    }

    private static ICameraProvider CreateCameraProvider(SettingsService settings)
    {
        var providerKey = settings.GetValue("CAMERA_PROVIDER", "simulated");
        if (string.Equals(providerKey, "canon", StringComparison.OrdinalIgnoreCase))
        {
            Log("CAMERA_PROVIDER=canon");
            return new CanonSdkCameraProvider();
        }

        Log("CAMERA_PROVIDER=simulated");
        return new SimulatedCameraProvider();
    }

    public static void Log(string message)
    {
        Session?.AppendLog(message);
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is MouseEventArgs
            || e.StagingItem.Input is TouchEventArgs
            || e.StagingItem.Input is KeyEventArgs)
        {
            ResetInactivityTimer();
        }
    }

    private void StartInactivityTimer()
    {
        _inactivityTimer = new DispatcherTimer
        {
            Interval = DefaultInactivityTimeout
        };
        _inactivityTimer.Tick += HandleInactivityTimeout;
        _inactivityTimer.Start();
    }

    private void ResetInactivityTimer()
    {
        if (_inactivityTimer is null)
        {
            return;
        }

        UpdateInactivityInterval();
        _inactivityTimer.Stop();
        _inactivityTimer.Start();
    }

    private void UpdateInactivityInterval()
    {
        if (_inactivityTimer is null)
        {
            return;
        }

        var screenKey = ResolveScreenKey();
        var seconds = _settings?.GetTimeoutSeconds(screenKey) ?? (int)DefaultInactivityTimeout.TotalSeconds;
        if (seconds <= 0)
        {
            seconds = (int)DefaultInactivityTimeout.TotalSeconds;
        }

        var nextInterval = TimeSpan.FromSeconds(seconds);
        if (_inactivityTimer.Interval != nextInterval)
        {
            _inactivityTimer.Interval = nextInterval;
        }
    }

    private string? ResolveScreenKey()
    {
        if (!string.IsNullOrWhiteSpace(Navigation?.CurrentKey))
        {
            return Navigation?.CurrentKey;
        }

        var screen = GetCurrentScreen();
        return string.Equals(screen, "unknown", StringComparison.OrdinalIgnoreCase) ? null : screen;
    }

    private void HandleInactivityTimeout(object? sender, EventArgs e)
    {
        _inactivityTimer?.Stop();

        var session = Session?.Current;
        if (session is null)
        {
            _inactivityTimer?.Start();
            return;
        }

        if (session.IsPaid && session.EndTime is null)
        {
            Session?.AppendLog("INACTIVITY_TIMEOUT_IGNORED paid=true");
            _inactivityTimer?.Start();
            return;
        }

        var screen = GetCurrentScreen();
        Session?.AppendLog($"INACTIVITY_TIMEOUT screen={screen}");

        if (string.Equals(screen, "startup", StringComparison.OrdinalIgnoreCase)
            && _settings is not null
            && !_settings.TestMode)
        {
            Session?.AppendLog("INACTIVITY_TIMEOUT_IGNORED startup=true");
            _inactivityTimer?.Start();
            return;
        }

        if (string.Equals(screen, "error", StringComparison.OrdinalIgnoreCase))
        {
            Session?.AppendLog("INACTIVITY_TIMEOUT_IGNORED error=true");
            _inactivityTimer?.Start();
            return;
        }

        if (!string.Equals(screen, "home", StringComparison.OrdinalIgnoreCase))
        {
            Navigation?.Navigate("home");
        }

        _inactivityTimer?.Start();
    }

    private void OnAnyButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var label = FormatLogValue(button.Content?.ToString());
        var screen = GetCurrentScreen();
        var command = button.Command?.GetType().Name ?? "none";
        var parameter = FormatLogValue(button.CommandParameter?.ToString());

        Session?.AppendLog($"BUTTON_CLICK label={label} screen={screen} command={command} param={parameter}");
    }

    private static string GetCurrentScreen()
    {
        if (Current?.MainWindow?.DataContext is MainViewModel main &&
            main.CurrentViewModel is ScreenViewModelBase screen)
        {
            return screen.ScreenKey;
        }

        if (Current?.MainWindow?.DataContext is MainViewModel fallback && fallback.CurrentViewModel is not null)
        {
            return fallback.CurrentViewModel.GetType().Name;
        }

        return "unknown";
    }

    private static string FormatLogValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "none";
        }

        var trimmed = value.Trim();
        return trimmed.Replace(' ', '_');
    }
}
