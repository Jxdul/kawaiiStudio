using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class FinalizeViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;

    public FinalizeViewModel(NavigationService navigation, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "finalize")
    {
        _navigation = navigation;
        ContinueCommand = new RelayCommand(() => _navigation.Navigate("printing"));
        BackCommand = new RelayCommand(() => _navigation.Navigate("review"));
    }

    public ICommand ContinueCommand { get; }
    public ICommand BackCommand { get; }
}
