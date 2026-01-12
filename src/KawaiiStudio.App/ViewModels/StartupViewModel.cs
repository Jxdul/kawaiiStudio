using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class StartupViewModel : ScreenViewModelBase
{
    private static readonly System.TimeSpan DeviceCheckTimeout = System.TimeSpan.FromSeconds(5);
    private readonly NavigationService _navigation;
    private readonly SettingsService _settings;
    private readonly CameraService _camera;
    private readonly CashAcceptorService _cashAcceptor;
    private readonly ErrorViewModel _errorViewModel;
    private readonly RelayCommand _retryCommand;
    private readonly RelayCommand _continueCommand;
    private readonly RelayCommand _forceContinueCommand;
    private readonly RelayCommand _enableTestModeCommand;
    private CancellationTokenSource? _cts;
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
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "startup")
    {
        _navigation = navigation;
        _settings = settings;
        _camera = camera;
        _cashAcceptor = cashAcceptor;
        _errorViewModel = errorViewModel;

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

        _ = RunChecksAsync(testMode, token);
    }

    private async Task RunChecksAsync(bool testMode, CancellationToken token)
    {
        var cameraOk = await CheckCameraAsync(testMode, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var cashOk = await CheckCashAsync(testMode, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var serverOk = await CheckPlaceholderAsync(Checks[2], "Server", testMode, token);
        if (token.IsCancellationRequested)
        {
            return;
        }

        var allOk = cashOk && serverOk;
        var hasErrors = !(cameraOk && cashOk && serverOk);
        UpdateCanContinue(allOk && !hasErrors, hasErrors);

        _isChecking = false;
        _retryCommand.RaiseCanExecuteChanged();
        _forceContinueCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanForceContinue));

        if (allOk && !hasErrors)
        {
            _navigation.Navigate("home");
        }
    }

    private void EnsureChecks()
    {
        if (Checks.Count > 0)
        {
            return;
        }

        Checks.Add(new StartupCheckItem("Camera"));
        Checks.Add(new StartupCheckItem("Cash reader"));
        Checks.Add(new StartupCheckItem("Server"));
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

        var item = Checks[0];
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

        var item = Checks[1];
        item.SetStatus("Checking...", string.Empty, false);

        if (testMode)
        {
            var allowedBills = _settings.CashDenominations;
            KawaiiStudio.App.App.Log("STARTUP_CASH_CHECK test_mode=true");

            var realProvider = new Rs232CashAcceptorProvider(_settings.CashCom, allowedBills);
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

    private ICashAcceptorProvider CreateCashProvider(bool testMode)
    {
        var allowedBills = _settings.CashDenominations;
        return testMode
            ? new SimulatedCashAcceptorProvider(allowedBills)
            : new Rs232CashAcceptorProvider(_settings.CashCom, allowedBills);
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

    private static Task<bool> CheckPlaceholderAsync(
        StartupCheckItem item,
        string name,
        bool testMode,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        item.SetStatus("Checking...", string.Empty, false);
        _ = testMode;

        KawaiiStudio.App.App.Log($"STARTUP_{name.ToUpperInvariant().Replace(' ', '_')}_OK");
        item.SetStatus("Connected", "Placeholder", true);
        return Task.FromResult(true);
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
        _navigation.Navigate("home");
    }

    private void ForceContinue()
    {
        if (!CanForceContinue)
        {
            return;
        }

        KawaiiStudio.App.App.Log("STARTUP_FORCE_CONTINUE");
        _navigation.Navigate("home");
    }

    private void EnableTestMode()
    {
        _settings.SetValue("TEST_MODE", "true");
        _settings.Save();
        KawaiiStudio.App.App.Log("STARTUP_ENABLE_TEST_MODE");
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
        }
    }

    public void SetStatus(string status, string detail, bool isOk)
    {
        Status = status;
        Detail = detail;
        IsOk = isOk;
    }
}
