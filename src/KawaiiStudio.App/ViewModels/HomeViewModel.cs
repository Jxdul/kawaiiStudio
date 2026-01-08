using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class HomeViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;

    public HomeViewModel(NavigationService navigation)
    {
        _navigation = navigation;
        StartCommand = new RelayCommand(() => _navigation.Navigate("library"));
    }

    public ICommand StartCommand { get; }
}
