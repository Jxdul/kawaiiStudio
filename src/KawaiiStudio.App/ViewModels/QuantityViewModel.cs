using System.Collections.Generic;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class QuantityViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;

    public QuantityViewModel(NavigationService navigation, SessionService session, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "quantity")
    {
        _navigation = navigation;
        _session = session;

        AvailableQuantities = new[] { 2, 4, 6, 8 };
        SelectQuantityCommand = new RelayCommand<int>(SelectQuantity);
        BackCommand = new RelayCommand(() => _navigation.Navigate("size"));
    }

    public IReadOnlyList<int> AvailableQuantities { get; }

    public ICommand SelectQuantityCommand { get; }
    public ICommand BackCommand { get; }

    private void SelectQuantity(int? quantity)
    {
        if (quantity is null)
        {
            return;
        }

        _session.Current.SetQuantity(quantity.Value);

        if (_session.Current.Size == PrintSize.FourBySix)
        {
            _navigation.Navigate("layout");
            return;
        }

        if (_session.Current.Size == PrintSize.TwoBySix)
        {
            _navigation.Navigate("category");
            return;
        }

        _navigation.Navigate("size");
    }
}
