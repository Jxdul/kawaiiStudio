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
        var session = new SessionService();
        var settings = new SettingsService(appPaths);

        var navigation = new NavigationService();
        var homeViewModel = new HomeViewModel(navigation, session, themeCatalog);
        var sizeViewModel = new SizeViewModel(navigation, session, themeCatalog);
        var quantityViewModel = new QuantityViewModel(navigation, session, themeCatalog, settings);
        var layoutViewModel = new LayoutViewModel(navigation, session, themeCatalog);
        var categoryViewModel = new CategoryViewModel(navigation, session, frameCatalog, themeCatalog);
        var frameViewModel = new FrameViewModel(navigation, session, themeCatalog);
        var paymentViewModel = new PaymentViewModel(navigation, session, themeCatalog, settings);
        var captureViewModel = new CaptureViewModel(navigation, themeCatalog);
        var reviewViewModel = new ReviewViewModel(navigation, themeCatalog);
        var finalizeViewModel = new FinalizeViewModel(navigation, themeCatalog);
        var printingViewModel = new PrintingViewModel(navigation, themeCatalog);
        var thankYouViewModel = new ThankYouViewModel(navigation, session, themeCatalog);
        var libraryViewModel = new LibraryViewModel(navigation, frameCatalog, themeCatalog, appPaths);
        var staffViewModel = new StaffViewModel(navigation, themeCatalog, settings);

        navigation.Register("home", homeViewModel);
        navigation.Register("size", sizeViewModel);
        navigation.Register("quantity", quantityViewModel);
        navigation.Register("layout", layoutViewModel);
        navigation.Register("category", categoryViewModel);
        navigation.Register("frame", frameViewModel);
        navigation.Register("payment", paymentViewModel);
        navigation.Register("capture", captureViewModel);
        navigation.Register("review", reviewViewModel);
        navigation.Register("finalize", finalizeViewModel);
        navigation.Register("printing", printingViewModel);
        navigation.Register("thank_you", thankYouViewModel);
        navigation.Register("library", libraryViewModel);
        navigation.Register("staff", staffViewModel);

        var mainViewModel = new MainViewModel(navigation);
        var window = new MainWindow { DataContext = mainViewModel };

        navigation.Navigate("home");
        window.Show();
    }
}
