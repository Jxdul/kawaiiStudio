using System.Collections.Generic;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class QuantityViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly SettingsService _settings;

    public QuantityViewModel(
        NavigationService navigation,
        SessionService session,
        ThemeCatalogService themeCatalog,
        SettingsService settings)
        : base(themeCatalog, "quantity")
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;

        AvailableQuantities = BuildQuantities();
        SelectQuantityCommand = new RelayCommand<int>(SelectQuantity);
        BackCommand = new RelayCommand(() => _navigation.Navigate("size"));
    }

    public IReadOnlyList<int> AvailableQuantities { get; }

    public ICommand SelectQuantityCommand { get; }
    public ICommand BackCommand { get; }

    private IReadOnlyList<int> BuildQuantities()
    {
        var maxQuantity = _settings.MaxQuantity;
        if (maxQuantity <= 0)
        {
            maxQuantity = 8;
        }

        var quantities = new List<int>();
        for (var value = 2; value <= maxQuantity; value += 2)
        {
            quantities.Add(value);
        }

        return quantities;
    }

    private void SelectQuantity(int quantity)
    {
        _session.Current.SetQuantity(quantity);
        KawaiiStudio.App.App.Log($"QUANTITY_SELECTED value={quantity}");

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
