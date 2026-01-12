using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class HomeViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly SettingsService _settings;
    private string _cameraDeviceStatus = "Unknown";
    private string _cashDeviceStatus = "Unknown";
    private string _cardDeviceStatus = "Unknown";

    public HomeViewModel(
        NavigationService navigation,
        SessionService session,
        SettingsService settings,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "home")
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;

        StartCommand = new RelayCommand(StartSession);
        AssetsCommand = new RelayCommand(() => _navigation.Navigate("library"));
        StaffCommand = new RelayCommand(() => _navigation.Navigate("staff"));
    }

    public ICommand StartCommand { get; }
    public ICommand AssetsCommand { get; }
    public ICommand StaffCommand { get; }

    public bool ShowDeviceStatus => _settings.TestMode;

    public string CameraDeviceStatus
    {
        get => _cameraDeviceStatus;
        private set
        {
            _cameraDeviceStatus = value;
            OnPropertyChanged();
        }
    }

    public string CashDeviceStatus
    {
        get => _cashDeviceStatus;
        private set
        {
            _cashDeviceStatus = value;
            OnPropertyChanged();
        }
    }

    public string CardDeviceStatus
    {
        get => _cardDeviceStatus;
        private set
        {
            _cardDeviceStatus = value;
            OnPropertyChanged();
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        _settings.Reload();
        OnPropertyChanged(nameof(ShowDeviceStatus));
        UpdateDeviceStatuses();
    }

    private void StartSession()
    {
        _session.StartNewSession();
        _navigation.Navigate("size");
    }

    private void UpdateDeviceStatuses()
    {
        CameraDeviceStatus = string.IsNullOrWhiteSpace(App.CameraDeviceStatus) ? "Unknown" : App.CameraDeviceStatus;
        CashDeviceStatus = string.IsNullOrWhiteSpace(App.CashDeviceStatus) ? "Unknown" : App.CashDeviceStatus;
        CardDeviceStatus = string.IsNullOrWhiteSpace(App.CardDeviceStatus) ? "Unknown" : App.CardDeviceStatus;
    }
}
