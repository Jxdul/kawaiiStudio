using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class FinalizeViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly FrameCompositionService _composer;
    private readonly VideoCompilationService _videoCompiler;
    private readonly RelayCommand _continueCommand;
    private string _statusText = "Preparing output...";
    private bool _canContinue;

    public FinalizeViewModel(
        NavigationService navigation,
        SessionService session,
        FrameCompositionService composer,
        VideoCompilationService videoCompiler,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "finalize")
    {
        _navigation = navigation;
        _session = session;
        _composer = composer;
        _videoCompiler = videoCompiler;
        _continueCommand = new RelayCommand(() => _navigation.Navigate("printing"), () => _canContinue);
        ContinueCommand = _continueCommand;
        BackCommand = new RelayCommand(() => _navigation.Navigate("review"));
    }

    public ICommand ContinueCommand { get; }
    public ICommand BackCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        KawaiiStudio.App.App.Log("FINALIZE_START");
        RenderComposite();
    }

    private async void RenderComposite()
    {
        _canContinue = false;
        _continueCommand.RaiseCanExecuteChanged();

        var outputPath = BuildOutputPath();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            StatusText = "Session output folder missing.";
            KawaiiStudio.App.App.Log("FINALIZE_COMPOSITE_FAILED reason=missing_session_folder");
            return;
        }

        if (_composer.TrySavePrintComposite(_session.Current, outputPath, out var error))
        {
            _session.SetFinalImagePath(outputPath);
            StatusText = "Composite ready. Rendering video...";
            var videoResult = await Task.Run(() =>
                _videoCompiler.TryBuildPreviewVideo(_session.Current, out var videoPath, out var videoError)
                    ? (success: true, path: videoPath, error: (string?)null)
                    : (success: false, path: (string?)null, error: videoError));

            if (videoResult.success && !string.IsNullOrWhiteSpace(videoResult.path))
            {
                _session.SetVideoPath(videoResult.path);
                StatusText = "Composite and video ready.";
                KawaiiStudio.App.App.Log($"FINALIZE_VIDEO_OK file={Path.GetFileName(videoResult.path)}");
            }
            else
            {
                StatusText = "Composite ready. Video failed.";
                var reason = string.IsNullOrWhiteSpace(videoResult.error) ? "unknown" : videoResult.error;
                KawaiiStudio.App.App.Log($"FINALIZE_VIDEO_FAILED reason={reason}");
            }

            _canContinue = true;
            _continueCommand.RaiseCanExecuteChanged();
            KawaiiStudio.App.App.Log($"FINALIZE_COMPOSITE_OK file={Path.GetFileName(outputPath)}");
            return;
        }

        StatusText = error ?? "Composite failed.";
        KawaiiStudio.App.App.Log($"FINALIZE_COMPOSITE_FAILED reason={error}");
    }

    private string? BuildOutputPath()
    {
        var sessionFolder = _session.Current.SessionFolder;
        if (string.IsNullOrWhiteSpace(sessionFolder))
        {
            return null;
        }

        var sessionId = string.IsNullOrWhiteSpace(_session.Current.SessionId)
            ? "session"
            : _session.Current.SessionId;
        var fileName = $"{sessionId}_final.png";
        return Path.Combine(sessionFolder, fileName);
    }
}
