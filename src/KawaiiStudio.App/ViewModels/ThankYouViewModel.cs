using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class ThankYouViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;

    public ThankYouViewModel(NavigationService navigation, SessionService session, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "thank_you")
    {
        _navigation = navigation;
        _session = session;
        DoneCommand = new RelayCommand(CompleteSession);
    }

    public ICommand DoneCommand { get; }

    private void CompleteSession()
    {
        _session.EndSession();
        _navigation.Navigate("home");
    }
}
