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
    private static readonly TimeSpan DefaultInactivityTimeout = TimeSpan.FromSeconds(30);
    private DispatcherTimer? _inactivityTimer;
    private DispatcherTimer? _inactivityCountdownTimer;
    private DateTime _inactivityDeadlineUtc = DateTime.MinValue;
    private int _lastTimeoutSeconds = -1;
    private SettingsService? _settings;
    private static readonly string[] TimeoutSuppressedScreens = { "home", "capture" };

    public static SessionService? Session { get; private set; }
    public static NavigationService? Navigation { get; private set; }
    public static SettingsService? Settings { get; private set; }
    public static int TimeoutSecondsRemaining { get; private set; }
    public static event Action<int>? TimeoutSecondsChanged;
    public static string CameraDeviceStatus { get; set; } = "Unknown";
    public static string CashDeviceStatus { get; set; } = "Unknown";
    public static string CardDeviceStatus { get; set; } = "Unknown";

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
        var videoCompiler = new VideoCompilationService(appPaths);
        session.PruneOldLogs(7);
        var settings = new SettingsService(appPaths);
        _settings = settings;
        Settings = settings;
        Session = session;
        var cameraProvider = CreateCameraProvider(settings);
        var cameraService = new CameraService(cameraProvider);
        var cashProvider = CreateCashAcceptorProvider(settings);
        var cashAcceptor = new CashAcceptorService(cashProvider);
        var cardPayment = CreateCardPaymentProvider(settings);
        var uploadService = new UploadService(settings);
        var printerProvider = new WindowsPrinterProvider(settings);
        var printerService = new PrinterService(settings, printerProvider);
        var qrCodes = new QrCodeService();
        var frameComposer = new FrameCompositionService(templateCatalog, qrCodes, frameOverrides);
        SetDeviceStatusFromProviders(cameraProvider, cashProvider, cardPayment);
        var assetPreloader = new AssetPreloadService(frameCatalog, themeCatalog);
        _ = assetPreloader.PreloadAsync();

        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(OnAnyButtonClicked));
        InputManager.Current.PreProcessInput += OnPreProcessInput;
        StartInactivityTimer();

        var navigation = new NavigationService(session);
        Navigation = navigation;
        navigation.Navigated += _ => ResetInactivityTimer();
        var errorViewModel = new ErrorViewModel();
        var startupViewModel = new StartupViewModel(navigation, settings, cameraService, cashAcceptor, errorViewModel, themeCatalog);
        var homeViewModel = new HomeViewModel(navigation, session, settings, themeCatalog);
        var sizeViewModel = new SizeViewModel(navigation, session, themeCatalog);
        var quantityViewModel = new QuantityViewModel(navigation, session, themeCatalog, settings);
        var layoutViewModel = new LayoutViewModel(navigation, session, themeCatalog);
        var categoryViewModel = new CategoryViewModel(navigation, session, frameCatalog, themeCatalog);
        var frameViewModel = new FrameViewModel(navigation, session, themeCatalog);
        var paymentViewModel = new PaymentViewModel(navigation, session, themeCatalog, settings, cashAcceptor, cardPayment);
        var captureViewModel = new CaptureViewModel(navigation, session, cameraService, settings, themeCatalog, templateCatalog);
        var processingViewModel = new ProcessingViewModel(navigation, themeCatalog);
        var reviewViewModel = new ReviewViewModel(navigation, session, frameComposer, themeCatalog);
        var finalizeViewModel = new FinalizeViewModel(navigation, session, frameComposer, videoCompiler, uploadService, themeCatalog);
        var printingViewModel = new PrintingViewModel(navigation, session, printerService, themeCatalog);
        var thankYouViewModel = new ThankYouViewModel(navigation, session, themeCatalog);
        var libraryViewModel = new LibraryViewModel(navigation, frameCatalog, themeCatalog, appPaths);
        var staffViewModel = new StaffViewModel(navigation, themeCatalog, settings, cameraService, cashAcceptor, cardPayment);
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
        navigation.Register("processing", processingViewModel);
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

    private static ICashAcceptorProvider CreateCashAcceptorProvider(SettingsService settings)
    {
        var allowedBills = settings.CashDenominations;
        if (settings.TestMode)
        {
            Log("CASH_PROVIDER=simulated");
            return new SimulatedCashAcceptorProvider(allowedBills);
        }

        Log($"CASH_PROVIDER=rs232 port={settings.CashCom}");
        return new Rs232CashAcceptorProvider(settings.CashCom, allowedBills, settings.CashLogAll);
    }

    private static ICardPaymentProvider CreateCardPaymentProvider(SettingsService settings)
    {
        var providerKey = settings.GetValue("CARD_PROVIDER", "simulated");
        if (string.Equals(providerKey, "stripe_terminal", StringComparison.OrdinalIgnoreCase))
        {
            Log("CARD_PROVIDER=stripe_terminal");
            return new StripeTerminalCardPaymentProvider(settings);
        }

        Log("CARD_PROVIDER=simulated");
        return new SimulatedCardPaymentProvider();
    }

    private static void SetDeviceStatusFromProviders(
        ICameraProvider cameraProvider,
        ICashAcceptorProvider cashProvider,
        ICardPaymentProvider cardPaymentProvider)
    {
        CameraDeviceStatus = cameraProvider is SimulatedCameraProvider ? "Simulated" : "Real";
        CashDeviceStatus = cashProvider is SimulatedCashAcceptorProvider ? "Simulated" : "Real";
        CardDeviceStatus = cardPaymentProvider is SimulatedCardPaymentProvider ? "Simulated" : "Real";
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
        StartInactivityCountdown();
        SetInactivityDeadline();
    }

    private void ResetInactivityTimer()
    {
        if (_inactivityTimer is null)
        {
            return;
        }

        UpdateInactivityInterval();
        if (IsTimeoutSuppressedScreen(ResolveScreenKey()))
        {
            _inactivityTimer.Stop();
            SetInactivityDeadline();
            return;
        }

        _inactivityTimer.Stop();
        _inactivityTimer.Start();
        SetInactivityDeadline();
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
            SetInactivityDeadline();
            return;
        }

        if (IsTimeoutSuppressedScreen(screen))
        {
            Session?.AppendLog($"INACTIVITY_TIMEOUT_IGNORED screen={screen}");
            SetInactivityDeadline();
            return;
        }

        if (string.Equals(screen, "review", StringComparison.OrdinalIgnoreCase))
        {
            if (TryHandleReviewTimeoutAdvance())
            {
                _inactivityTimer?.Start();
                SetInactivityDeadline();
                return;
            }
        }

        var target = GetTimeoutAdvanceTarget(screen);
        if (!string.IsNullOrWhiteSpace(target))
        {
            Navigation?.Navigate(target);
            _inactivityTimer?.Start();
            SetInactivityDeadline();
            return;
        }

        if (!string.Equals(screen, "home", StringComparison.OrdinalIgnoreCase))
        {
            Navigation?.Navigate("home");
        }

        _inactivityTimer?.Start();
        SetInactivityDeadline();
    }

    private void StartInactivityCountdown()
    {
        _inactivityCountdownTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _inactivityCountdownTimer.Tick -= HandleCountdownTick;
        _inactivityCountdownTimer.Tick += HandleCountdownTick;
        _inactivityCountdownTimer.Start();
    }

    private void HandleCountdownTick(object? sender, EventArgs e)
    {
        UpdateTimeoutRemaining();
    }

    private void SetInactivityDeadline()
    {
        if (_inactivityTimer is null)
        {
            return;
        }

        _inactivityDeadlineUtc = DateTime.UtcNow + _inactivityTimer.Interval;
        UpdateTimeoutRemaining();
    }

    private void UpdateTimeoutRemaining()
    {
        var remainingSeconds = 0;
        if (_inactivityTimer is not null && _inactivityTimer.IsEnabled)
        {
            var remaining = _inactivityDeadlineUtc - DateTime.UtcNow;
            remainingSeconds = remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;
        }

        if (remainingSeconds == _lastTimeoutSeconds)
        {
            return;
        }

        _lastTimeoutSeconds = remainingSeconds;
        TimeoutSecondsRemaining = remainingSeconds;
        TimeoutSecondsChanged?.Invoke(remainingSeconds);
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

    private static bool IsTimeoutSuppressedScreen(string? screen)
    {
        if (string.IsNullOrWhiteSpace(screen))
        {
            return false;
        }

        foreach (var suppressed in TimeoutSuppressedScreens)
        {
            if (string.Equals(screen, suppressed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetTimeoutAdvanceTarget(string screen)
    {
        if (string.IsNullOrWhiteSpace(screen))
        {
            return null;
        }

        return screen.ToLowerInvariant() switch
        {
            "review" => "finalize",
            "processing" => "review",
            "finalize" => "printing",
            "printing" => "thank_you",
            "thank_you" => "home",
            _ => null
        };
    }

    private bool TryHandleReviewTimeoutAdvance()
    {
        if (Current?.MainWindow?.DataContext is not MainViewModel main)
        {
            return false;
        }

        if (main.CurrentViewModel is not ReviewViewModel review)
        {
            return false;
        }

        review.AutoFillMissingSelections();
        Navigation?.Navigate("finalize");
        return true;
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
