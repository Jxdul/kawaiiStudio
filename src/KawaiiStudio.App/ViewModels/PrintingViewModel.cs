using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class PrintingViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private string? _printPreviewPath;

    public PrintingViewModel(
        NavigationService navigation,
        SessionService session,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "printing")
    {
        _navigation = navigation;
        _session = session;
        ContinueCommand = new RelayCommand(() => _navigation.Navigate("thank_you"));
    }

    public ICommand ContinueCommand { get; }

    public string? PrintPreviewPath
    {
        get => _printPreviewPath;
        private set
        {
            _printPreviewPath = value;
            OnPropertyChanged();
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        KawaiiStudio.App.App.Log("PRINTING_START");
        PrintPreviewPath = _session.Current.FinalImagePath;
    }
}
