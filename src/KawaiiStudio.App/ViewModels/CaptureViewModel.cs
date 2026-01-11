using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class CaptureViewModel : ScreenViewModelBase
{
    private const int ShotCount = 8;
    private const int CountdownSeconds = 3;
    private const int SecondsBetweenShots = 4;

    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly ICameraProvider _camera;
    private readonly RelayCommand _continueCommand;
    private CancellationTokenSource? _captureCts;
    private bool _isCapturing;
    private string _statusText = "Ready to capture 8 photos";
    private string _progressText = "Shots: 0 / 8";
    private string _buttonText = "Start Capture";

    public CaptureViewModel(
        NavigationService navigation,
        SessionService session,
        ICameraProvider camera,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "capture")
    {
        _navigation = navigation;
        _session = session;
        _camera = camera;
        _continueCommand = new RelayCommand(StartCapture, () => !_isCapturing);
        ContinueCommand = _continueCommand;
        BackCommand = new RelayCommand(() => _navigation.Navigate("payment"));
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

    public string ProgressText
    {
        get => _progressText;
        private set
        {
            _progressText = value;
            OnPropertyChanged();
        }
    }

    public string CaptureButtonText
    {
        get => _buttonText;
        private set
        {
            _buttonText = value;
            OnPropertyChanged();
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        KawaiiStudio.App.App.Log("CAPTURE_START");
        ResetState();
    }

    private void ResetState()
    {
        _captureCts?.Cancel();
        _isCapturing = false;
        StatusText = "Ready to capture 8 photos";
        ProgressText = "Shots: 0 / 8";
        CaptureButtonText = "Start Capture";
        _continueCommand.RaiseCanExecuteChanged();
    }

    private async void StartCapture()
    {
        if (_isCapturing)
        {
            return;
        }

        _isCapturing = true;
        _continueCommand.RaiseCanExecuteChanged();
        _session.Current.ClearCapturedPhotos();
        _session.Current.ClearSelectedMapping();
        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;

        KawaiiStudio.App.App.Log($"CAPTURE_SEQUENCE_START shots={ShotCount} countdown={CountdownSeconds} interval={SecondsBetweenShots}");

        try
        {
            if (!_camera.IsConnected)
            {
                StatusText = "Connecting to camera...";
                if (!await _camera.ConnectAsync(token))
                {
                    StatusText = "Camera not connected";
                    KawaiiStudio.App.App.Log("CAPTURE_CAMERA_CONNECT_FAILED");
                    return;
                }
            }

            for (var remaining = CountdownSeconds; remaining > 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                StatusText = $"Starting in {remaining}...";
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }

            for (var shotIndex = 1; shotIndex <= ShotCount; shotIndex++)
            {
                token.ThrowIfCancellationRequested();
                StatusText = $"Capturing photo {shotIndex} of {ShotCount}";
                ProgressText = $"Shots: {shotIndex} / {ShotCount}";

                var path = BuildShotPath(shotIndex);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var captured = await _camera.CapturePhotoAsync(path, token);
                    if (captured)
                    {
                        _session.RegisterCapturedPhoto(path);
                        KawaiiStudio.App.App.Log($"CAPTURE_SHOT index={shotIndex} file={Path.GetFileName(path)}");
                    }
                    else
                    {
                        KawaiiStudio.App.App.Log($"CAPTURE_SHOT_FAILED index={shotIndex} file={Path.GetFileName(path)}");
                    }
                }
                else
                {
                    KawaiiStudio.App.App.Log($"CAPTURE_SHOT_FAILED index={shotIndex} reason=no_session_folder");
                }

                if (shotIndex < ShotCount)
                {
                    await Task.Delay(TimeSpan.FromSeconds(SecondsBetweenShots), token);
                }
            }

            StatusText = "Capture complete";
            CaptureButtonText = "Continue";
            KawaiiStudio.App.App.Log("CAPTURE_SEQUENCE_COMPLETE");
            _navigation.Navigate("review");
        }
        catch (OperationCanceledException)
        {
            KawaiiStudio.App.App.Log("CAPTURE_SEQUENCE_CANCELED");
        }
        finally
        {
            _isCapturing = false;
            _continueCommand.RaiseCanExecuteChanged();
        }
    }

    private string? BuildShotPath(int shotIndex)
    {
        var photosFolder = _session.Current.PhotosFolder ?? _session.Current.SessionFolder;
        if (string.IsNullOrWhiteSpace(photosFolder))
        {
            return null;
        }

        var sessionId = string.IsNullOrWhiteSpace(_session.Current.SessionId) ? "session" : _session.Current.SessionId;
        var fileName = $"{sessionId}_shot_{shotIndex:00}.png";
        return Path.Combine(photosFolder, fileName);
    }

}
