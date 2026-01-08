using System.Collections.ObjectModel;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class LibraryViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly FrameCatalogService _frameCatalog;
    private readonly ThemeCatalogService _themeCatalog;

    public LibraryViewModel(
        NavigationService navigation,
        FrameCatalogService frameCatalog,
        ThemeCatalogService themeCatalog,
        AppPaths appPaths)
    {
        _navigation = navigation;
        _frameCatalog = frameCatalog;
        _themeCatalog = themeCatalog;
        FramesRoot = appPaths.FramesRoot;
        ThemeRoot = appPaths.ThemeRoot;

        BackCommand = new RelayCommand(() => _navigation.Navigate("home"));
        RescanCommand = new RelayCommand(LoadAssets);

        LoadAssets();
    }

    public string FramesRoot { get; }
    public string ThemeRoot { get; }

    public ObservableCollection<FrameCategory> FrameCategories { get; } = new();
    public ObservableCollection<ThemeBackground> Backgrounds { get; } = new();

    public ICommand BackCommand { get; }
    public ICommand RescanCommand { get; }

    public bool HasFrames => FrameCategories.Count > 0;
    public bool HasBackgrounds => Backgrounds.Count > 0;

    private void LoadAssets()
    {
        FrameCategories.Clear();
        foreach (var category in _frameCatalog.Load())
        {
            FrameCategories.Add(category);
        }

        Backgrounds.Clear();
        foreach (var background in _themeCatalog.LoadBackgrounds())
        {
            Backgrounds.Add(background);
        }

        OnPropertyChanged(nameof(HasFrames));
        OnPropertyChanged(nameof(HasBackgrounds));
    }
}
