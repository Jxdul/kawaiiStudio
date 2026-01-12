using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class CaptureViewModel : ScreenViewModelBase
{
    private const int ShotCount = 8;
    private const int CountdownSeconds = 3;
    private const int SecondsBetweenShots = 4;
    private static readonly TimeSpan PreviewFrameInterval = TimeSpan.FromSeconds(1);

    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly ICameraProvider _camera;
    private readonly RelayCommand _continueCommand;
    private DispatcherTimer? _liveViewTimer;
    private CancellationTokenSource? _liveViewCts;
    private CancellationTokenSource? _captureCts;
    private int _previewFrameIndex;
    private DateTime _lastPreviewFrameUtc = DateTime.MinValue;
    private int _previewSaveInProgress;
    private bool _isLiveViewTicking;
    private bool _isCapturingShot;
    private bool _isCapturing;
    private string _statusText = "Ready to capture 8 photos";
    private string _progressText = "Shots: 0 / 8";
    private string _buttonText = "Start Capture";
    private ImageSource? _liveViewImage;

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
        BackCommand = new RelayCommand(NavigateBack);
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

    public ImageSource? LiveViewImage
    {
        get => _liveViewImage;
        private set
        {
            _liveViewImage = value;
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

    private async void StartLiveView()
    {
        _liveViewCts?.Cancel();
        _liveViewCts = new CancellationTokenSource();
        var token = _liveViewCts.Token;

        if (!_camera.IsConnected)
        {
            var connected = await _camera.ConnectAsync(token);
            if (!connected || token.IsCancellationRequested)
            {
                return;
            }
        }

        var started = await _camera.StartLiveViewAsync(token);
        if (!started || token.IsCancellationRequested)
        {
            return;
        }

        _liveViewTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _liveViewTimer.Tick -= OnLiveViewTick;
        _liveViewTimer.Tick += OnLiveViewTick;
        _liveViewTimer.Start();
    }

    private async void OnLiveViewTick(object? sender, EventArgs e)
    {
        if (_isLiveViewTicking || _isCapturingShot || _liveViewCts is null)
        {
            return;
        }

        _isLiveViewTicking = true;
        var token = _liveViewCts.Token;
        try
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            var frame = await _camera.GetLiveViewFrameAsync(token);
            if (frame is not null)
            {
                LiveViewImage = frame;
                QueuePreviewFrameSave(frame);
            }
        }
        catch
        {
            // Ignore transient live view errors.
        }
        finally
        {
            _isLiveViewTicking = false;
        }
    }

    private void StopLiveView()
    {
        if (_liveViewTimer is not null)
        {
            _liveViewTimer.Stop();
            _liveViewTimer.Tick -= OnLiveViewTick;
        }

        _liveViewCts?.Cancel();
        _liveViewCts = null;
        LiveViewImage = null;
        _ = _camera.StopLiveViewAsync(CancellationToken.None);
    }

    private async void StartCapture()
    {
        if (_isCapturing)
        {
            return;
        }

        _isCapturing = true;
        _continueCommand.RaiseCanExecuteChanged();
        ResetPreviewFrames();
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

            StartLiveView();
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
                    _isCapturingShot = true;
                    var captured = await _camera.CapturePhotoAsync(path, token);
                    _isCapturingShot = false;
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
            StopLiveView();
            _navigation.Navigate("review");
        }
        catch (OperationCanceledException)
        {
            KawaiiStudio.App.App.Log("CAPTURE_SEQUENCE_CANCELED");
        }
        finally
        {
            _isCapturingShot = false;
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

    private void NavigateBack()
    {
        StopLiveView();
        _navigation.Navigate("payment");
    }

    private void ResetPreviewFrames()
    {
        _previewFrameIndex = 0;
        _lastPreviewFrameUtc = DateTime.MinValue;
    }

    private void QueuePreviewFrameSave(BitmapSource frame)
    {
        if (!_isCapturing)
        {
            return;
        }

        var folder = _session.Current.PreviewFramesFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastPreviewFrameUtc < PreviewFrameInterval)
        {
            return;
        }

        if (Interlocked.Exchange(ref _previewSaveInProgress, 1) == 1)
        {
            return;
        }

        _lastPreviewFrameUtc = now;
        var index = ++_previewFrameIndex;
        if (frame.CanFreeze && !frame.IsFrozen)
        {
            frame.Freeze();
        }

        var frozenFrame = frame;

        _ = Task.Run(() => SavePreviewFrame(frozenFrame, folder, index))
            .ContinueWith(_ => Interlocked.Exchange(ref _previewSaveInProgress, 0), TaskScheduler.Default);
    }

    private static void SavePreviewFrame(BitmapSource frame, string folder, int index)
    {
        Directory.CreateDirectory(folder);
        var fileName = $"preview_{index:0000}.png";
        var path = Path.Combine(folder, fileName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }
}
