using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Net.Http;
using System.Printing;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class StartupViewModel : ScreenViewModelBase
{
    private const int CheckCameraIndex = 0;
    private const int CheckCameraLiveViewIndex = 1;
    private const int CheckCashIndex = 2;
    private const int CheckCashAcceptIndex = 3;
    private const int CheckPrinterIndex = 4;
    private const int CheckInternetIndex = 5;
    private const int CheckServerIndex = 6;
    private const int CheckBoothRegistrationIndex = 7;
    private const int CheckUploadIndex = 8;
    private const int CheckFrameSyncIndex = 9;
    private static readonly System.TimeSpan DeviceCheckTimeout = System.TimeSpan.FromSeconds(5);
    private static readonly HttpClient Http = new()
    {
        Timeout = DeviceCheckTimeout
    };
    private readonly NavigationService _navigation;
    private readonly SettingsService _settings;
    private readonly CameraService _camera;
    private readonly CashAcceptorService _cashAcceptor;
    private readonly ErrorViewModel _errorViewModel;
    private readonly BoothRegistrationService _boothRegistration;
    private readonly FrameSyncService _frameSync;
    private readonly RelayCommand _retryCommand;
    private readonly RelayCommand _continueCommand;
    private readonly RelayCommand _forceContinueCommand;
    private readonly RelayCommand _enableTestModeCommand;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _autoContinueCts;
    private bool _isChecking;
    private bool _canContinue;
    private bool _hasErrors;
    private string _modeText = "Test Mode: Off";

    public StartupViewModel(
        NavigationService navigation,
        SettingsService settings,
        CameraService camera,
        CashAcceptorService cashAcceptor,
        ErrorViewModel errorViewModel,
        ThemeCatalogService themeCatalog,
        BoothRegistrationService boothRegistration,
        FrameSyncService frameSync)
        : base(themeCatalog, "startup")
    {
        _navigation = navigation;
        _settings = settings;
        _camera = camera;
        _cashAcceptor = cashAcceptor;
        _errorViewModel = errorViewModel;
        _boothRegistration = boothRegistration;
        _frameSync = frameSync;

        _retryCommand = new RelayCommand(StartChecks, () => !_isChecking);
        RetryCommand = _retryCommand;
        _continueCommand = new RelayCommand(Continue, () => _canContinue);
        ContinueCommand = _continueCommand;
        _forceContinueCommand = new RelayCommand(ForceContinue, () => CanForceContinue);
        ForceContinueCommand = _forceContinueCommand;
        _enableTestModeCommand = new RelayCommand(EnableTestMode);
        EnableTestModeCommand = _enableTestModeCommand;
    }

    public ObservableCollection<StartupCheckItem> Checks { get; } = new();

    public ICommand RetryCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand ForceContinueCommand { get; }
    public ICommand EnableTestModeCommand { get; }

    public bool CanForceContinue => !_isChecking && _hasErrors;

    public bool IsTestMode => _settings.TestMode;

    public string ModeText
    {
        get => _modeText;
        private set
        {
            _modeText = value;
            OnPropertyChanged();
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        StartChecks();
    }

    private void StartChecks()
    {
        if (_isChecking)
        {
            _cts?.Cancel();
        }

        _autoContinueCts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _isChecking = true;
        _retryCommand.RaiseCanExecuteChanged();
        UpdateCanContinue(false, hasErrors: false);

        EnsureChecks();
        ResetStatuses();

        _settings.Reload();
        var testMode = _settings.TestMode;
        ModeText = testMode ? "Test Mode: On" : "Test Mode: Off";
        OnPropertyChanged(nameof(IsTestMode));

        _ = RunChecksAsync(testMode, token);
    }

    private async Task RunChecksAsync(bool testMode, CancellationToken token)
    {
        var cameraOk = await CheckCameraAsync(testMode, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var liveViewOk = await CheckCameraLiveViewAsync(Checks[CheckCameraLiveViewIndex], cameraOk, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var cashOk = await CheckCashAsync(testMode, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var cashAcceptOk = await CheckCashAcceptAsync(Checks[CheckCashAcceptIndex], cashOk, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var printerOk = await CheckPrinterQueuesAsync(Checks[CheckPrinterIndex], token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var internetOk = await CheckInternetAsync(Checks[CheckInternetIndex], token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var serverOk = await CheckServerAsync(Checks[CheckServerIndex], token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var boothRegistrationOk = await CheckBoothRegistrationAsync(Checks[CheckBoothRegistrationIndex], token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var uploadOk = await CheckUploadAsync(Checks[CheckUploadIndex], token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var frameSyncOk = await CheckFrameSyncAsync(Checks[CheckFrameSyncIndex], token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var allOk = cameraOk
            && liveViewOk
            && cashOk
            && cashAcceptOk
            && printerOk
            && internetOk
            && serverOk
            && boothRegistrationOk
            && uploadOk
            && frameSyncOk;
        UpdateCanContinue(allOk, !allOk);
        
        // Note: Heartbeat timer is now started immediately after successful registration
        // in CheckBoothRegistrationAsync, so it runs even if other checks fail
        
        // Cancel any pending auto-continue (removed auto-continue feature)
        _autoContinueCts?.Cancel();

        _isChecking = false;
        _retryCommand.RaiseCanExecuteChanged();
        _forceContinueCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanForceContinue));

        // Stay on startup screen until user explicitly continues.
    }

    private void EnsureChecks()
    {
        if (Checks.Count > 0)
        {
            return;
        }

        Checks.Add(new StartupCheckItem("Camera"));
        Checks.Add(new StartupCheckItem("Camera live view"));
        Checks.Add(new StartupCheckItem("Cash reader"));
        Checks.Add(new StartupCheckItem("Cash accept"));
        Checks.Add(new StartupCheckItem("Printer queue"));
        Checks.Add(new StartupCheckItem("Internet"));
        Checks.Add(new StartupCheckItem("Server"));
        Checks.Add(new StartupCheckItem("Booth registration"));
        Checks.Add(new StartupCheckItem("Upload endpoint"));
        Checks.Add(new StartupCheckItem("Frame sync"));
    }

    private void SetAllStatus(string status, string detail, bool isOk)
    {
        foreach (var item in Checks)
        {
            item.SetStatus(status, detail, isOk);
        }
    }

    private void ResetStatuses()
    {
        foreach (var item in Checks)
        {
            item.SetStatus("Pending", string.Empty, false);
        }
    }

    private async Task<bool> CheckCameraAsync(bool testMode, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        var item = Checks[CheckCameraIndex];
        _camera.UseProvider(CreateCameraProvider());
        if (testMode)
        {
            item.SetStatus("Checking...", "Test mode", false);
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_CHECK test_mode=true");

            var deadline = System.DateTime.UtcNow + DeviceCheckTimeout;
            var realAttempt = await RunCheckWithTimeout(_camera.ConnectAsync, DeviceCheckTimeout, token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            if (realAttempt.ok)
            {
                KawaiiStudio.App.App.CameraDeviceStatus = "Real";
                item.SetStatus("Connected", "Test mode", true);
                KawaiiStudio.App.App.Log("STARTUP_CAMERA_OK test_mode=true");
                return true;
            }

            if (realAttempt.timedOut)
            {
                item.SetStatus("Failed", "Timeout", false);
                KawaiiStudio.App.App.Log("STARTUP_CAMERA_TIMEOUT");
                return false;
            }

            KawaiiStudio.App.App.Log("STARTUP_CAMERA_FALLBACK simulated");
            _camera.UseProvider(new SimulatedCameraProvider());
            var remaining = deadline - System.DateTime.UtcNow;
            if (remaining <= System.TimeSpan.Zero)
            {
                item.SetStatus("Failed", "Timeout", false);
                KawaiiStudio.App.App.Log("STARTUP_CAMERA_TIMEOUT");
                return false;
            }

            var simulatedAttempt = await RunCheckWithTimeout(_camera.ConnectAsync, remaining, token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            if (simulatedAttempt.ok)
            {
                KawaiiStudio.App.App.CameraDeviceStatus = "Simulated";
                item.SetStatus("Simulated", "Camera unavailable", true);
                return true;
            }

            if (simulatedAttempt.timedOut)
            {
                item.SetStatus("Failed", "Timeout", false);
                KawaiiStudio.App.App.Log("STARTUP_CAMERA_TIMEOUT");
                return false;
            }

            item.SetStatus("Failed", "Camera not connected", false);
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_FAILED");
            return false;
        }

        item.SetStatus("Checking...", string.Empty, false);
        KawaiiStudio.App.App.Log("STARTUP_CAMERA_CHECK");

        var realAttemptNonTest = await RunCheckWithTimeout(_camera.ConnectAsync, DeviceCheckTimeout, token);
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (realAttemptNonTest.ok)
        {
            KawaiiStudio.App.App.CameraDeviceStatus = "Real";
            item.SetStatus("Connected", string.Empty, true);
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_OK");
            return true;
        }

        if (realAttemptNonTest.timedOut)
        {
            item.SetStatus("Failed", "Timeout", false);
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_TIMEOUT");
        }
        else
        {
            item.SetStatus("Failed", "Camera not connected", false);
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_FAILED");
        }

        if (!testMode && !token.IsCancellationRequested)
        {
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_CONTINUE allowed=true");
        }

        return false;
    }

    private async Task<bool> CheckCameraLiveViewAsync(
        StartupCheckItem item,
        bool cameraOk,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (!cameraOk)
        {
            item.SetStatus("Skipped", "Camera unavailable", false);
            KawaiiStudio.App.App.Log("STARTUP_LIVEVIEW_SKIP");
            return false;
        }

        item.SetStatus("Checking...", "Live view start/stop", false);
        KawaiiStudio.App.App.Log("STARTUP_LIVEVIEW_CHECK");

        var startAttempt = await RunCheckWithTimeout(_camera.StartLiveViewAsync, DeviceCheckTimeout, token);
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (!startAttempt.ok)
        {
            if (startAttempt.timedOut)
            {
                item.SetStatus("Failed", "Start timeout", false);
                KawaiiStudio.App.App.Log("STARTUP_LIVEVIEW_START_TIMEOUT");
            }
            else
            {
                item.SetStatus("Failed", "Live view failed", false);
                KawaiiStudio.App.App.Log("STARTUP_LIVEVIEW_START_FAILED");
            }

            return false;
        }

        try
        {
            var stopTask = _camera.StopLiveViewAsync(token);
            var completed = await Task.WhenAny(stopTask, Task.Delay(DeviceCheckTimeout, token));
            if (completed != stopTask)
            {
                item.SetStatus("Failed", "Stop timeout", false);
                KawaiiStudio.App.App.Log("STARTUP_LIVEVIEW_STOP_TIMEOUT");
                return false;
            }

            await stopTask;
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }

            item.SetStatus("Failed", "Stop canceled", false);
            KawaiiStudio.App.App.Log("STARTUP_LIVEVIEW_STOP_CANCELED");
            return false;
        }
        catch
        {
            item.SetStatus("Failed", "Stop error", false);
            KawaiiStudio.App.App.Log("STARTUP_LIVEVIEW_STOP_FAILED");
            return false;
        }

        item.SetStatus("Connected", "Live view ok", true);
        KawaiiStudio.App.App.Log("STARTUP_LIVEVIEW_OK");
        return true;
    }

    private ICameraProvider CreateCameraProvider()
    {
        var providerKey = _settings.GetValue("CAMERA_PROVIDER", "simulated");
        if (string.Equals(providerKey, "canon", System.StringComparison.OrdinalIgnoreCase))
        {
            return new CanonSdkCameraProvider();
        }

        return new SimulatedCameraProvider();
    }

    private async Task<bool> CheckCashAsync(bool testMode, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        var item = Checks[CheckCashIndex];
        item.SetStatus("Checking...", string.Empty, false);

        if (testMode)
        {
            var allowedBills = _settings.CashDenominations;
            KawaiiStudio.App.App.Log("STARTUP_CASH_CHECK test_mode=true");

            var realProvider = new Rs232CashAcceptorProvider(_settings.CashCom, allowedBills, _settings.CashLogAll);
            var realAttempt = await TryCashProviderAsync(realProvider, token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            if (realAttempt.ok)
            {
                KawaiiStudio.App.App.CashDeviceStatus = "Real";
                item.SetStatus("Connected", $"{_settings.CashCom} (test mode)", true);
                KawaiiStudio.App.App.Log("STARTUP_CASH_OK test_mode=true provider=rs232");
                return true;
            }

            if (realAttempt.timedOut)
            {
                KawaiiStudio.App.App.Log("STARTUP_CASH_RS232_TIMEOUT test_mode=true");
            }
            else
            {
                KawaiiStudio.App.App.Log("STARTUP_CASH_RS232_FAILED test_mode=true");
            }

            KawaiiStudio.App.App.Log("STARTUP_CASH_FALLBACK simulated");
            var simulatedProvider = new SimulatedCashAcceptorProvider(allowedBills);
            var simulatedAttempt = await TryCashProviderAsync(simulatedProvider, token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            if (simulatedAttempt.ok)
            {
                KawaiiStudio.App.App.CashDeviceStatus = "Simulated";
                item.SetStatus("Simulated", "Fallback (test mode)", true);
                KawaiiStudio.App.App.Log("STARTUP_CASH_SIMULATED");
                return true;
            }

            if (simulatedAttempt.timedOut)
            {
                item.SetStatus("Failed", "Timeout", false);
                KawaiiStudio.App.App.Log("STARTUP_CASH_TIMEOUT");
            }
            else
            {
                item.SetStatus("Failed", "Cash reader not connected", false);
                KawaiiStudio.App.App.Log("STARTUP_CASH_FAILED");
            }

            return false;
        }

        var cashAttempt = await TryCashProviderAsync(CreateCashProvider(false), token);
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (cashAttempt.ok)
        {
            KawaiiStudio.App.App.CashDeviceStatus = "Real";
            item.SetStatus("Connected", _settings.CashCom, true);
            KawaiiStudio.App.App.Log("STARTUP_CASH_OK");
            return true;
        }

        if (cashAttempt.timedOut)
        {
            item.SetStatus("Failed", "Timeout", false);
            KawaiiStudio.App.App.Log("STARTUP_CASH_TIMEOUT");
        }
        else
        {
            item.SetStatus("Failed", "Cash reader not connected", false);
            KawaiiStudio.App.App.Log("STARTUP_CASH_FAILED");
        }

        return false;
    }

    private async Task<bool> CheckCashAcceptAsync(
        StartupCheckItem item,
        bool cashOk,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (!cashOk)
        {
            item.SetStatus("Skipped", "Cash reader unavailable", false);
            KawaiiStudio.App.App.Log("STARTUP_CASH_ACCEPT_SKIP");
            return false;
        }

        item.SetStatus("Checking...", "Enable/disable", false);
        KawaiiStudio.App.App.Log("STARTUP_CASH_ACCEPT_CHECK");

        try
        {
            _cashAcceptor.UpdateRemainingAmount(1m);
            await Task.Delay(200, token);
            _cashAcceptor.UpdateRemainingAmount(0m);
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }

            item.SetStatus("Failed", "Canceled", false);
            KawaiiStudio.App.App.Log("STARTUP_CASH_ACCEPT_CANCELED");
            return false;
        }
        catch
        {
            item.SetStatus("Failed", "Toggle failed", false);
            KawaiiStudio.App.App.Log("STARTUP_CASH_ACCEPT_FAILED");
            return false;
        }

        item.SetStatus("Connected", "Accept toggle ok", true);
        KawaiiStudio.App.App.Log("STARTUP_CASH_ACCEPT_OK");
        return true;
    }

    private Task<bool> CheckPrinterQueuesAsync(StartupCheckItem item, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        item.SetStatus("Checking...", "Printer queues", false);
        KawaiiStudio.App.App.Log("STARTUP_PRINTER_CHECK");

        var details = new List<string>();
        var ok2x6 = TryResolvePrinterQueue(_settings.PrinterName2x6, "2x6", details);
        var ok4x6 = TryResolvePrinterQueue(_settings.PrinterName4x6, "4x6", details);

        if (token.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        if (ok2x6 && ok4x6)
        {
            item.SetStatus("Connected", "Queues ready", true);
            KawaiiStudio.App.App.Log("STARTUP_PRINTER_OK");
            return Task.FromResult(true);
        }

        item.SetStatus("Failed", string.Join("; ", details), false);
        KawaiiStudio.App.App.Log("STARTUP_PRINTER_FAILED");
        return Task.FromResult(false);
    }

    private static bool TryResolvePrinterQueue(
        string printerName,
        string label,
        List<string> details)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            details.Add($"{label}: name missing");
            return false;
        }

        try
        {
            var queue = new PrintQueue(new PrintServer(), printerName);
            queue.Refresh();
            
            // Check if printer is offline
            if (queue.IsOffline)
            {
                details.Add($"{label}: offline");
                return false;
            }

            // Check queue status for error conditions
            var status = queue.QueueStatus;
            var errorFlags = PrintQueueStatus.Offline
                | PrintQueueStatus.Error
                | PrintQueueStatus.NotAvailable
                | PrintQueueStatus.PaperOut
                | PrintQueueStatus.PaperProblem
                | PrintQueueStatus.DoorOpen
                | PrintQueueStatus.ManualFeed
                | PrintQueueStatus.PaperJam
                | PrintQueueStatus.OutputBinFull
                | PrintQueueStatus.Paused
                | PrintQueueStatus.TonerLow
                | PrintQueueStatus.NoToner
                | PrintQueueStatus.UserIntervention;

            if ((status & errorFlags) != PrintQueueStatus.None)
            {
                var statusMessages = new List<string>();
                if ((status & PrintQueueStatus.Offline) != PrintQueueStatus.None)
                    statusMessages.Add("offline");
                if ((status & PrintQueueStatus.Error) != PrintQueueStatus.None)
                    statusMessages.Add("error");
                if ((status & PrintQueueStatus.NotAvailable) != PrintQueueStatus.None)
                    statusMessages.Add("not available");
                if ((status & PrintQueueStatus.PaperOut) != PrintQueueStatus.None)
                    statusMessages.Add("paper out");
                if ((status & PrintQueueStatus.PaperProblem) != PrintQueueStatus.None)
                    statusMessages.Add("paper problem");
                if ((status & PrintQueueStatus.DoorOpen) != PrintQueueStatus.None)
                    statusMessages.Add("door open");
                if ((status & PrintQueueStatus.PaperJam) != PrintQueueStatus.None)
                    statusMessages.Add("paper jam");
                if ((status & PrintQueueStatus.ManualFeed) != PrintQueueStatus.None)
                    statusMessages.Add("manual feed required");
                if ((status & PrintQueueStatus.UserIntervention) != PrintQueueStatus.None)
                    statusMessages.Add("needs attention");

                var statusText = statusMessages.Count > 0 
                    ? string.Join(", ", statusMessages) 
                    : "unavailable";
                details.Add($"{label}: {statusText}");
                return false;
            }

            // Try to access the queue to verify it's actually accessible
            try
            {
                _ = queue.DefaultPrintTicket;
            }
            catch
            {
                details.Add($"{label}: cannot access");
                return false;
            }

            details.Add($"{label}: {queue.Name}");
            return true;
        }
        catch (System.Exception ex)
        {
            var reason = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            details.Add($"{label}: {reason}");
            return false;
        }
    }

    private ICashAcceptorProvider CreateCashProvider(bool testMode)
    {
        var allowedBills = _settings.CashDenominations;
        return testMode
            ? new SimulatedCashAcceptorProvider(allowedBills)
            : new Rs232CashAcceptorProvider(_settings.CashCom, allowedBills, _settings.CashLogAll);
    }

    private async Task<(bool ok, bool timedOut)> TryCashProviderAsync(
        ICashAcceptorProvider provider,
        CancellationToken token)
    {
        _cashAcceptor.UseProvider(provider);
        var attempt = await RunCheckWithTimeout(_cashAcceptor.ConnectAsync, DeviceCheckTimeout, token);
        if (token.IsCancellationRequested)
        {
            return (false, false);
        }

        try
        {
            await _cashAcceptor.DisconnectAsync(token);
        }
        catch
        {
            // Ignore disconnect errors during startup checks.
        }

        return attempt;
    }

    private async Task<bool> CheckServerAsync(StartupCheckItem item, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        item.SetStatus("Checking...", string.Empty, false);
        var baseUrl = _settings.StripeTerminalBaseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            item.SetStatus("Failed", "Missing STRIPE_TERMINAL_BASE_URL", false);
            KawaiiStudio.App.App.Log("STARTUP_SERVER_FAILED reason=missing_base_url");
            return false;
        }

        var url = $"{baseUrl}/health";

        try
        {
            using var response = await Http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
            {
                item.SetStatus("Failed", "Server not reachable", false);
                KawaiiStudio.App.App.Log($"STARTUP_SERVER_FAILED status={(int)response.StatusCode}");
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(token);
            if (string.IsNullOrWhiteSpace(body))
            {
                item.SetStatus("Failed", "Empty response", false);
                KawaiiStudio.App.App.Log("STARTUP_SERVER_FAILED reason=empty_response");
                return false;
            }

            using var json = JsonDocument.Parse(body);
            var ok = json.RootElement.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
            if (!ok)
            {
                item.SetStatus("Failed", "Server unhealthy", false);
                KawaiiStudio.App.App.Log("STARTUP_SERVER_FAILED reason=unhealthy");
                return false;
            }

            item.SetStatus("Connected", "Cloudflare worker", true);
            KawaiiStudio.App.App.Log("STARTUP_SERVER_OK");
            return true;
        }
        catch (TaskCanceledException)
        {
            item.SetStatus("Failed", "Timeout", false);
            KawaiiStudio.App.App.Log("STARTUP_SERVER_TIMEOUT");
            return false;
        }
        catch
        {
            item.SetStatus("Failed", "Server error", false);
            KawaiiStudio.App.App.Log("STARTUP_SERVER_FAILED");
            return false;
        }
    }

    private async Task<bool> CheckInternetAsync(StartupCheckItem item, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        item.SetStatus("Checking...", "Cloudflare ping", false);
        KawaiiStudio.App.App.Log("STARTUP_INTERNET_CHECK");

        try
        {
            using var response = await Http.GetAsync("https://cloudflare.com/cdn-cgi/trace", token);
            if (!response.IsSuccessStatusCode)
            {
                item.SetStatus("Failed", $"HTTP {(int)response.StatusCode}", false);
                KawaiiStudio.App.App.Log($"STARTUP_INTERNET_FAILED status={(int)response.StatusCode}");
                return false;
            }

            item.SetStatus("Connected", "Cloudflare reachable", true);
            KawaiiStudio.App.App.Log("STARTUP_INTERNET_OK");
            return true;
        }
        catch (TaskCanceledException)
        {
            item.SetStatus("Failed", "Timeout", false);
            KawaiiStudio.App.App.Log("STARTUP_INTERNET_TIMEOUT");
            return false;
        }
        catch
        {
            item.SetStatus("Failed", "Network error", false);
            KawaiiStudio.App.App.Log("STARTUP_INTERNET_FAILED");
            return false;
        }
    }

    private async Task<bool> CheckBoothRegistrationAsync(StartupCheckItem item, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        var boothId = _settings.BoothId?.Trim();
        if (string.IsNullOrWhiteSpace(boothId))
        {
            item.SetStatus("Failed", "Missing BOOTH_ID", false);
            KawaiiStudio.App.App.Log("STARTUP_BOOTH_REGISTRATION_FAILED reason=missing_booth_id");
            return false;
        }

        item.SetStatus("Checking...", "Booth registration", false);
        KawaiiStudio.App.App.Log("STARTUP_BOOTH_REGISTRATION_CHECK");

        var (ok, isAllowed, error) = await _boothRegistration.CheckBoothAllowedAsync(token);
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (!ok)
        {
            var errorMessage = error switch
            {
                "missing_base_url" => "Missing server URL",
                "missing_booth_id" => "Missing booth ID",
                "timeout" => "Timeout",
                _ => "Server error"
            };
            item.SetStatus("Failed", errorMessage, false);
            KawaiiStudio.App.App.Log($"STARTUP_BOOTH_REGISTRATION_FAILED reason={error}");
            return false;
        }

        if (!isAllowed)
        {
            item.SetStatus("Failed", "Booth ID not allowed", false);
            KawaiiStudio.App.App.Log("STARTUP_BOOTH_REGISTRATION_FAILED reason=booth_not_allowed");
            return false;
        }

        var (registerOk, registerError) = await _boothRegistration.RegisterAsync(cancellationToken: token);
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (!registerOk)
        {
            var errorMessage = registerError switch
            {
                "booth_not_allowed" => "Booth ID not allowed",
                "missing_base_url" => "Missing server URL",
                "timeout" => "Timeout",
                _ => "Registration failed"
            };
            item.SetStatus("Failed", errorMessage, false);
            KawaiiStudio.App.App.Log($"STARTUP_BOOTH_REGISTRATION_FAILED reason={registerError}");
            return false;
        }

        item.SetStatus("Registered", $"Booth: {boothId}", true);
        KawaiiStudio.App.App.Log($"STARTUP_BOOTH_REGISTRATION_OK boothId={boothId}");
        
        // Start heartbeat timer immediately after successful registration
        // This ensures heartbeats are sent even if other checks fail
        _boothRegistration.StartHeartbeatTimer();
        
        return true;
    }

    private async Task<bool> CheckUploadAsync(StartupCheckItem item, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (!_settings.UploadEnabled)
        {
            item.SetStatus("Disabled", "Upload off", true);
            KawaiiStudio.App.App.Log("STARTUP_UPLOAD_DISABLED");
            return true;
        }

        var baseUrl = _settings.UploadBaseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            item.SetStatus("Failed", "Missing UPLOAD_BASE_URL", false);
            KawaiiStudio.App.App.Log("STARTUP_UPLOAD_FAILED reason=missing_base_url");
            return false;
        }

        item.SetStatus("Checking...", "Upload health", false);
        KawaiiStudio.App.App.Log("STARTUP_UPLOAD_CHECK");

        var url = $"{baseUrl}/health";

        try
        {
            using var response = await Http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode)
            {
                item.SetStatus("Failed", "Upload not reachable", false);
                KawaiiStudio.App.App.Log($"STARTUP_UPLOAD_FAILED status={(int)response.StatusCode}");
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(token);
            if (string.IsNullOrWhiteSpace(body))
            {
                item.SetStatus("Failed", "Empty response", false);
                KawaiiStudio.App.App.Log("STARTUP_UPLOAD_FAILED reason=empty_response");
                return false;
            }

            using var json = JsonDocument.Parse(body);
            var ok = json.RootElement.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();
            if (!ok)
            {
                item.SetStatus("Failed", "Upload unhealthy", false);
                KawaiiStudio.App.App.Log("STARTUP_UPLOAD_FAILED reason=unhealthy");
                return false;
            }

            item.SetStatus("Connected", "Upload ok", true);
            KawaiiStudio.App.App.Log("STARTUP_UPLOAD_OK");
            return true;
        }
        catch (TaskCanceledException)
        {
            item.SetStatus("Failed", "Timeout", false);
            KawaiiStudio.App.App.Log("STARTUP_UPLOAD_TIMEOUT");
            return false;
        }
        catch
        {
            item.SetStatus("Failed", "Upload error", false);
            KawaiiStudio.App.App.Log("STARTUP_UPLOAD_FAILED");
            return false;
        }
    }

    private async Task<bool> CheckFrameSyncAsync(StartupCheckItem item, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        item.SetStatus("Checking...", "Syncing frames", false);
        KawaiiStudio.App.App.Log("STARTUP_FRAME_SYNC_CHECK");

        try
        {
            var (ok, error) = await _frameSync.SyncFramesAsync(token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            if (!ok)
            {
                var errorMessage = error switch
                {
                    "missing_base_url" => "Missing server URL",
                    "timeout" => "Timeout",
                    _ => "Sync failed"
                };
                item.SetStatus("Failed", errorMessage, false);
                KawaiiStudio.App.App.Log($"STARTUP_FRAME_SYNC_FAILED reason={error}");
                // Don't block startup if sync fails - frames may still work
                return true;
            }

            item.SetStatus("Synced", "Frames updated", true);
            KawaiiStudio.App.App.Log("STARTUP_FRAME_SYNC_OK");
            return true;
        }
        catch (TaskCanceledException)
        {
            item.SetStatus("Failed", "Timeout", false);
            KawaiiStudio.App.App.Log("STARTUP_FRAME_SYNC_TIMEOUT");
            // Don't block startup on timeout
            return true;
        }
        catch
        {
            item.SetStatus("Failed", "Sync error", false);
            KawaiiStudio.App.App.Log("STARTUP_FRAME_SYNC_FAILED");
            // Don't block startup on error
            return true;
        }
    }

    private void UpdateCanContinue(bool canContinue, bool hasErrors)
    {
        _canContinue = canContinue;
        _hasErrors = hasErrors;
        _continueCommand.RaiseCanExecuteChanged();
        _forceContinueCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanForceContinue));
    }

    private void Continue()
    {
        _autoContinueCts?.Cancel();
        _navigation.Navigate("home");
    }

    private void ForceContinue()
    {
        if (!CanForceContinue)
        {
            return;
        }

        _autoContinueCts?.Cancel();
        KawaiiStudio.App.App.Log("STARTUP_FORCE_CONTINUE");
        _navigation.Navigate("home");
    }

    private void EnableTestMode()
    {
        _settings.SetValue("TEST_MODE", "true");
        _settings.Save();
        KawaiiStudio.App.App.Log("STARTUP_ENABLE_TEST_MODE");
        OnPropertyChanged(nameof(IsTestMode));
        StartChecks();
    }


    private static async Task<(bool ok, bool timedOut)> RunCheckWithTimeout(
        Func<CancellationToken, Task<bool>> action,
        System.TimeSpan timeout,
        CancellationToken token)
    {
        if (timeout <= System.TimeSpan.Zero)
        {
            return (false, true);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var task = action(timeoutCts.Token);
            var completed = await Task.WhenAny(task, Task.Delay(timeout, token));
            if (completed == task)
            {
                return (await task, false);
            }
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested)
            {
                return (false, false);
            }
        }
        catch
        {
            return (false, false);
        }

        if (token.IsCancellationRequested)
        {
            return (false, false);
        }

        return (false, true);
    }
}

public sealed class StartupCheckItem : ViewModelBase
{
    private string _status = "Pending";
    private string _detail = string.Empty;
    private bool _isOk;

    public StartupCheckItem(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(State));
        }
    }

    public string Detail
    {
        get => _detail;
        private set
        {
            _detail = value;
            OnPropertyChanged();
        }
    }

    public bool IsOk
    {
        get => _isOk;
        private set
        {
            _isOk = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(State));
        }
    }

    public StartupCheckState State
    {
        get
        {
            if (string.Equals(Status, "Pending", System.StringComparison.OrdinalIgnoreCase))
            {
                return StartupCheckState.Pending;
            }

            if (Status.StartsWith("Checking", System.StringComparison.OrdinalIgnoreCase))
            {
                return StartupCheckState.Testing;
            }

            return IsOk ? StartupCheckState.Success : StartupCheckState.Error;
        }
    }

    public void SetStatus(string status, string detail, bool isOk)
    {
        Status = status;
        Detail = detail;
        IsOk = isOk;
    }
}

public enum StartupCheckState
{
    Pending,
    Testing,
    Success,
    Error
}
