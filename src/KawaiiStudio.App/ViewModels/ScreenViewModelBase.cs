using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public abstract class ScreenViewModelBase : ViewModelBase, INavigationAware
{
    private readonly ThemeCatalogService _themeCatalog;
    private string? _backgroundPath;

    protected ScreenViewModelBase(ThemeCatalogService themeCatalog, string screenKey)
    {
        _themeCatalog = themeCatalog;
        ScreenKey = screenKey;
    }

    public string ScreenKey { get; }

    public string? BackgroundPath
    {
        get => _backgroundPath;
        private set
        {
            _backgroundPath = value;
            OnPropertyChanged();
        }
    }

    public virtual void OnNavigatedTo()
    {
        BackgroundPath = _themeCatalog.GetBackgroundPath(ScreenKey);
    }
}
