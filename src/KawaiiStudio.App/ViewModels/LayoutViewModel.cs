using System.Collections.Generic;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class LayoutViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;

    public LayoutViewModel(NavigationService navigation, SessionService session, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "layout")
    {
        _navigation = navigation;
        _session = session;

        Options = new List<LayoutOption>
        {
            new(LayoutStyle.TwoSlots, "Style A (2 shots)", 2, "4x6_2slots"),
            new(LayoutStyle.FourSlots, "Style B (4 shots)", 4, "4x6_4slots"),
            new(LayoutStyle.SixSlots, "Style C (6 shots)", 6, "4x6_6slots")
        };

        SelectLayoutCommand = new RelayCommand<LayoutOption>(SelectLayout);
        BackCommand = new RelayCommand(() => _navigation.Navigate("quantity"));
    }

    public IReadOnlyList<LayoutOption> Options { get; }

    public ICommand SelectLayoutCommand { get; }
    public ICommand BackCommand { get; }

    private void SelectLayout(LayoutOption option)
    {
        _session.Current.SetLayout(option.Style);
        KawaiiStudio.App.App.Log($"LAYOUT_SELECTED value={option.TemplateType}");
        _navigation.Navigate("category");
    }
}
