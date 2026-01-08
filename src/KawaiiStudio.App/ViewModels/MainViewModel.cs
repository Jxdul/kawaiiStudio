using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private ViewModelBase? _currentViewModel;

    public MainViewModel(NavigationService navigation)
    {
        _navigation = navigation;
        _navigation.Navigated += OnNavigated;
    }

    public ViewModelBase? CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            _currentViewModel = value;
            OnPropertyChanged();
        }
    }

    private void OnNavigated(ViewModelBase viewModel)
    {
        CurrentViewModel = viewModel;
    }
}
