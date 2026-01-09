using System.Collections.ObjectModel;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class CategoryViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly FrameCatalogService _frameCatalog;

    public CategoryViewModel(
        NavigationService navigation,
        SessionService session,
        FrameCatalogService frameCatalog,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "category")
    {
        _navigation = navigation;
        _session = session;
        _frameCatalog = frameCatalog;

        SelectCategoryCommand = new RelayCommand<FrameCategory>(SelectCategory);
        BackCommand = new RelayCommand(NavigateBack);
    }

    public ObservableCollection<FrameCategory> Categories { get; } = new();

    public ICommand SelectCategoryCommand { get; }
    public ICommand BackCommand { get; }

    public bool HasCategories => Categories.Count > 0;

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        LoadCategories();
    }

    private void LoadCategories()
    {
        Categories.Clear();
        var templateType = _session.Current.TemplateType;
        foreach (var category in _frameCatalog.Load())
        {
            if (templateType is null || category.TemplateType.Equals(templateType, System.StringComparison.OrdinalIgnoreCase))
            {
                Categories.Add(category);
            }
        }

        OnPropertyChanged(nameof(HasCategories));
    }

    private void SelectCategory(FrameCategory category)
    {
        _session.Current.SetCategory(category);
        _navigation.Navigate("frame");
    }

    private void NavigateBack()
    {
        if (_session.Current.Size == PrintSize.FourBySix)
        {
            _navigation.Navigate("layout");
            return;
        }

        _navigation.Navigate("quantity");
    }
}
