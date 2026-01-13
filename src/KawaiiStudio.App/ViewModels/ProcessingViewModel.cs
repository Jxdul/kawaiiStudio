using System.Threading;
using System.Threading.Tasks;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class ProcessingViewModel : ScreenViewModelBase
{
    private const int TransitionDelayMilliseconds = 600;
    private readonly NavigationService _navigation;
    private CancellationTokenSource? _cts;

    public ProcessingViewModel(
        NavigationService navigation,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "processing")
    {
        _navigation = navigation;
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        KawaiiStudio.App.App.Log("PROCESSING_START");
        StartTransition();
    }

    private async void StartTransition()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await Task.Delay(TransitionDelayMilliseconds, token);
            if (!token.IsCancellationRequested)
            {
                _navigation.Navigate("review");
            }
        }
        catch (TaskCanceledException)
        {
            // Ignore navigation cancellation.
        }
    }
}
