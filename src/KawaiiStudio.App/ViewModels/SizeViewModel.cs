using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class SizeViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;

    public SizeViewModel(NavigationService navigation, SessionService session, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "size")
    {
        _navigation = navigation;
        _session = session;

        SelectSizeCommand = new RelayCommand<PrintSize>(SelectSize);
        BackCommand = new RelayCommand(() => _navigation.Navigate("home"));
    }

    public ICommand SelectSizeCommand { get; }
    public ICommand BackCommand { get; }

    private void SelectSize(PrintSize? size)
    {
        if (size is null)
        {
            return;
        }

        _session.Current.SetSize(size.Value);
        _navigation.Navigate("quantity");
    }
}
