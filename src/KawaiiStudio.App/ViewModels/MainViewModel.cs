using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private ViewModelBase? _currentViewModel;
    private int _timeoutSecondsRemaining;
    private bool _isTimeoutVisible;

    public MainViewModel(NavigationService navigation)
    {
        _navigation = navigation;
        _navigation.Navigated += OnNavigated;
        App.TimeoutSecondsChanged += OnTimeoutSecondsChanged;
        _timeoutSecondsRemaining = App.TimeoutSecondsRemaining;
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

    public int TimeoutSecondsRemaining
    {
        get => _timeoutSecondsRemaining;
        private set
        {
            if (_timeoutSecondsRemaining == value)
            {
                return;
            }

            _timeoutSecondsRemaining = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimeoutLabel));
        }
    }

    public string TimeoutLabel => $"Time left: {_timeoutSecondsRemaining}s";

    public bool IsTimeoutVisible
    {
        get => _isTimeoutVisible;
        private set
        {
            if (_isTimeoutVisible == value)
            {
                return;
            }

            _isTimeoutVisible = value;
            OnPropertyChanged();
        }
    }

    private void OnNavigated(ViewModelBase viewModel)
    {
        CurrentViewModel = viewModel;
        UpdateTimeoutVisibility();
    }

    private void OnTimeoutSecondsChanged(int seconds)
    {
        TimeoutSecondsRemaining = seconds;
        UpdateTimeoutVisibility();
    }

    private void UpdateTimeoutVisibility()
    {
        var screen = CurrentViewModel as ScreenViewModelBase;
        var visible = screen is not null
            && !string.Equals(screen.ScreenKey, "error", System.StringComparison.OrdinalIgnoreCase)
            && _timeoutSecondsRemaining > 0;
        IsTimeoutVisible = visible;
    }
}
