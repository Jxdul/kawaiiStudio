using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class StartupViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SettingsService _settings;
    private readonly CameraService _camera;
    private readonly CashAcceptorService _cashAcceptor;
    private readonly ErrorViewModel _errorViewModel;
    private readonly RelayCommand _retryCommand;
    private readonly RelayCommand _continueCommand;
    private readonly RelayCommand _enableTestModeCommand;
    private CancellationTokenSource? _cts;
    private bool _isChecking;
    private bool _canContinue;
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
        _enableTestModeCommand = new RelayCommand(EnableTestMode);
        EnableTestModeCommand = _enableTestModeCommand;
    }

    public ObservableCollection<StartupCheckItem> Checks { get; } = new();

    public ICommand RetryCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand EnableTestModeCommand { get; }

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
        UpdateCanContinue(false);

        EnsureChecks();
        SetAllStatus("Checking...", string.Empty, isOk: false);

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
        UpdateCanContinue(allOk);

        _isChecking = false;
        _retryCommand.RaiseCanExecuteChanged();

        if (allOk)
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

    private async Task<bool> CheckCameraAsync(bool testMode, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        var item = Checks[0];
        if (testMode)
        {
            _camera.UseProvider(new SimulatedCameraProvider());
            var simulated = await _camera.ConnectAsync(token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            item.SetStatus("Simulated", "Test mode", simulated);
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_SIMULATED");
            return simulated;
        }

        item.SetStatus("Connecting...", string.Empty, false);
        KawaiiStudio.App.App.Log("STARTUP_CAMERA_CHECK");

        var connected = await _camera.ConnectAsync(token);
        if (token.IsCancellationRequested)
        {
            return false;
        }

        if (connected)
        {
            item.SetStatus("Connected", string.Empty, true);
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_OK");
            return true;
        }

        if (testMode)
        {
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_FALLBACK simulated");
            _camera.UseProvider(new SimulatedCameraProvider());
            var simulated = await _camera.ConnectAsync(token);
            if (token.IsCancellationRequested)
            {
                return false;
            }

            if (simulated)
            {
                item.SetStatus("Simulated", "Camera unavailable", true);
                return true;
            }
        }

        item.SetStatus("Failed", "Camera not connected", false);
        KawaiiStudio.App.App.Log("STARTUP_CAMERA_FAILED");

        if (!testMode && !token.IsCancellationRequested)
        {
            KawaiiStudio.App.App.Log("STARTUP_CAMERA_CONTINUE allowed=true");
        }

        return false;
    }

    private async Task<bool> CheckCashAsync(bool testMode, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return false;
        }

        var item = Checks[1];
        item.SetStatus("Connecting...", string.Empty, false);

        _cashAcceptor.UseProvider(CreateCashProvider(testMode));
        var connected = await _cashAcceptor.ConnectAsync(token);
        if (token.IsCancellationRequested)
        {
            return false;
        }
        try
        {
            await _cashAcceptor.DisconnectAsync(token);
        }
        catch
        {
            // Ignore disconnect errors during startup checks.
        }

        if (connected)
        {
            var status = testMode ? "Simulated" : "Connected";
            var detail = testMode ? "Test mode" : _settings.CashCom;
            item.SetStatus(status, detail, true);
            KawaiiStudio.App.App.Log(testMode ? "STARTUP_CASH_SIMULATED" : "STARTUP_CASH_OK");
            return true;
        }

        item.SetStatus("Failed", "Cash reader not connected", false);
        KawaiiStudio.App.App.Log("STARTUP_CASH_FAILED");
        return false;
    }

    private ICashAcceptorProvider CreateCashProvider(bool testMode)
    {
        return testMode
            ? new SimulatedCashAcceptorProvider()
            : new Rs232CashAcceptorProvider(_settings.CashCom);
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

    private void UpdateCanContinue(bool canContinue)
    {
        _canContinue = canContinue;
        _continueCommand.RaiseCanExecuteChanged();
    }

    private void Continue()
    {
        _navigation.Navigate("home");
    }

    private void EnableTestMode()
    {
        _settings.SetValue("TEST_MODE", "true");
        _settings.Save();
        KawaiiStudio.App.App.Log("STARTUP_ENABLE_TEST_MODE");
        StartChecks();
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
