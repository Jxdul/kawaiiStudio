using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class ReviewViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;

    public ReviewViewModel(NavigationService navigation, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "review")
    {
        _navigation = navigation;
        ContinueCommand = new RelayCommand(() => _navigation.Navigate("finalize"));
        BackCommand = new RelayCommand(() => _navigation.Navigate("capture"));
    }

    public ICommand ContinueCommand { get; }
    public ICommand BackCommand { get; }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        KawaiiStudio.App.App.Log("REVIEW_START");
    }
}
