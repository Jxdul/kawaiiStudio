using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class CaptureViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;

    public CaptureViewModel(NavigationService navigation, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "capture")
    {
        _navigation = navigation;
        ContinueCommand = new RelayCommand(() => _navigation.Navigate("review"));
        BackCommand = new RelayCommand(() => _navigation.Navigate("payment"));
    }

    public ICommand ContinueCommand { get; }
    public ICommand BackCommand { get; }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        KawaiiStudio.App.App.Log("CAPTURE_START");
    }
}
