using System.Windows;
using KawaiiStudio.App.Services;
using KawaiiStudio.App.ViewModels;

namespace KawaiiStudio.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appPaths = AppPaths.Resolve();
        var frameCatalog = new FrameCatalogService(appPaths.FramesRoot);
        var themeCatalog = new ThemeCatalogService(appPaths.ThemeRoot);

        var navigation = new NavigationService();
        var homeViewModel = new HomeViewModel(navigation);
        var libraryViewModel = new LibraryViewModel(navigation, frameCatalog, themeCatalog, appPaths);

        navigation.Register("home", homeViewModel);
        navigation.Register("library", libraryViewModel);

        var mainViewModel = new MainViewModel(navigation);
        var window = new MainWindow { DataContext = mainViewModel };

        navigation.Navigate("home");
        window.Show();
    }
}
