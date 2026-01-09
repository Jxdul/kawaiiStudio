using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class HomeViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;

    public HomeViewModel(NavigationService navigation, SessionService session, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "home")
    {
        _navigation = navigation;
        _session = session;

        StartCommand = new RelayCommand(StartSession);
        AssetsCommand = new RelayCommand(() => _navigation.Navigate("library"));
    }

    public ICommand StartCommand { get; }
    public ICommand AssetsCommand { get; }

    private void StartSession()
    {
        _session.StartNewSession();
        _navigation.Navigate("size");
    }
}
