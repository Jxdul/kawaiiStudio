using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class PrintingViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;

    public PrintingViewModel(NavigationService navigation, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "printing")
    {
        _navigation = navigation;
        ContinueCommand = new RelayCommand(() => _navigation.Navigate("thank_you"));
    }

    public ICommand ContinueCommand { get; }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        KawaiiStudio.App.App.Log("PRINTING_START");
    }
}
