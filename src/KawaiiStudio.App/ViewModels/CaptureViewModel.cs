using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class CaptureViewModel : ScreenViewModelBase
{
    private const int ShotCount = 8;
    private const int MaxCountdownSeconds = 30;
    private static readonly TimeSpan LiveViewInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan PreviewFrameInterval = TimeSpan.FromMilliseconds(200);

    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly ICameraProvider _camera;
    private readonly SettingsService _settings;
    private readonly TemplateCatalogService _templates;
    private readonly RelayCommand _continueCommand;
    private DispatcherTimer? _liveViewTimer;
    private CancellationTokenSource? _liveViewCts;
    private CancellationTokenSource? _captureCts;
    private int _previewFrameIndex;
    private DateTime _lastPreviewFrameUtc = DateTime.MinValue;
    private DateTime _suspendLiveViewUntilUtc = DateTime.MinValue;
    private int _previewSaveInProgress;
    private bool _isLiveViewTicking;
    private bool _isCapturing;
    private int _countdownSecondsRemaining;
    private string _statusText = "Ready to capture 8 photos";
    private string _progressText = "Shots: 0 / 8";
    private string _buttonText = "Start Capture";
    private ImageSource? _liveViewImage;
    private int _imagesRemaining = ShotCount;
    private bool _showStartButton = true;

    public CaptureViewModel(
        NavigationService navigation,
        SessionService session,
        ICameraProvider camera,
        SettingsService settings,
        ThemeCatalogService themeCatalog,
        TemplateCatalogService templates)
        : base(themeCatalog, "capture")
    {
        _navigation = navigation;
        _session = session;
        _camera = camera;
        _settings = settings;
        _templates = templates;
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

    public int ImagesRemaining
    {
        get => _imagesRemaining;
        private set
        {
            _imagesRemaining = value;
            OnPropertyChanged();
        }
    }

    public bool ShowStartButton
    {
        get => _showStartButton;
        private set
        {
            _showStartButton = value;
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
        SetCountdown(0);
        StatusText = "Ready to capture 8 photos";
        ProgressText = "Shots: 0 / 8";
        CaptureButtonText = "Start Capture";
        ImagesRemaining = ShotCount;
        ShowStartButton = true;
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
            Interval = LiveViewInterval
        };
        _liveViewTimer.Tick -= OnLiveViewTick;
        _liveViewTimer.Tick += OnLiveViewTick;
        _liveViewTimer.Start();
    }

    private async void OnLiveViewTick(object? sender, EventArgs e)
    {
        if (_isLiveViewTicking || _liveViewCts is null)
        {
            return;
        }

        if (DateTime.UtcNow < _suspendLiveViewUntilUtc)
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
                var displayFrame = ApplyLiveViewCrop(frame) ?? frame;
                LiveViewImage = displayFrame;
                QueuePreviewFrameSave(displayFrame);
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
        ShowStartButton = false;
        _continueCommand.RaiseCanExecuteChanged();
        ResetPreviewFrames();
        _session.Current.ClearCapturedPhotos();
        _session.Current.ClearSelectedMapping();
        ImagesRemaining = ShotCount;
        _captureCts = new CancellationTokenSource();
        var token = _captureCts.Token;

        var countdownSeconds = GetCameraTimerSeconds();
        KawaiiStudio.App.App.Log($"CAPTURE_SEQUENCE_START shots={ShotCount} countdown={countdownSeconds} interval={countdownSeconds}");

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

            var photosFolder = _session.Current.PhotosFolder;
            if (!string.IsNullOrWhiteSpace(photosFolder))
            {
                Directory.CreateDirectory(photosFolder);
            }

            StartLiveView();
            for (var remaining = countdownSeconds; remaining > 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                SetCountdown(remaining);
                StatusText = $"Starting in {remaining}...";
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
            SetCountdown(0);

            for (var shotIndex = 1; shotIndex <= ShotCount; shotIndex++)
            {
                token.ThrowIfCancellationRequested();
                StatusText = $"Capturing photo {shotIndex} of {ShotCount}";
                ProgressText = $"Shots: {shotIndex} / {ShotCount}";
                
                // Update images remaining (how many will remain after this shot)
                ImagesRemaining = ShotCount - shotIndex;

                var path = BuildShotPath(shotIndex);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    SuspendLiveView(TimeSpan.FromMilliseconds(500));
                    var captured = await _camera.CapturePhotoAsync(path, token);
                    if (captured)
                    {
                        var resolvedPath = ResolveCapturedPath(path);
                        if (!string.IsNullOrWhiteSpace(resolvedPath))
                        {
                            _session.RegisterCapturedPhoto(resolvedPath);
                            KawaiiStudio.App.App.Log($"CAPTURE_SHOT index={shotIndex} file={Path.GetFileName(resolvedPath)}");
                            // Images remaining already updated before capture
                        }
                        else
                        {
                            KawaiiStudio.App.App.Log($"CAPTURE_SHOT_MISSING index={shotIndex} file={Path.GetFileName(path)}");
                        }
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
                    for (var remaining = countdownSeconds; remaining > 0; remaining--)
                    {
                        token.ThrowIfCancellationRequested();
                        SetCountdown(remaining);
                        StatusText = $"Next shot in {remaining}...";
                        await Task.Delay(TimeSpan.FromSeconds(1), token);
                    }

                    SetCountdown(0);
                }
            }

            StatusText = "Capture complete";
            CaptureButtonText = "Continue";
            KawaiiStudio.App.App.Log("CAPTURE_SEQUENCE_COMPLETE");
            StopLiveView();
            _navigation.Navigate("processing");
        }
        catch (OperationCanceledException)
        {
            KawaiiStudio.App.App.Log("CAPTURE_SEQUENCE_CANCELED");
        }
        finally
        {
            _isCapturing = false;
            SetCountdown(0);
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
        var fileName = $"{sessionId}_shot_{shotIndex:00}.jpg";
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
        _suspendLiveViewUntilUtc = DateTime.MinValue;
    }

    private void SuspendLiveView(TimeSpan duration)
    {
        var until = DateTime.UtcNow.Add(duration);
        if (until > _suspendLiveViewUntilUtc)
        {
            _suspendLiveViewUntilUtc = until;
        }
    }

    private static string? ResolveCapturedPath(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        var baseName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return null;
        }

        var matches = Directory.GetFiles(folder, $"{baseName}.*");
        if (matches.Length == 0)
        {
            return null;
        }

        return matches[0];
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

    private BitmapSource? ApplyLiveViewCrop(BitmapSource source)
    {
        var templateType = _session.Current.TemplateType;
        if (string.IsNullOrWhiteSpace(templateType))
        {
            return null;
        }

        if (!templateType.StartsWith("4x6_4slots", StringComparison.OrdinalIgnoreCase)
            && !templateType.StartsWith("4x6_6slots", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var template = _templates.GetTemplate(templateType);
        if (template is null || template.Slots.Count == 0)
        {
            return null;
        }

        var slot = template.Slots[0];
        if (slot.Width <= 0 || slot.Height <= 0)
        {
            return null;
        }

        var targetAspect = slot.Width / (double)slot.Height;
        var sourceWidth = source.PixelWidth;
        var sourceHeight = source.PixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        var sourceAspect = sourceWidth / (double)sourceHeight;
        var cropWidth = sourceWidth;
        var cropHeight = sourceHeight;

        if (sourceAspect > targetAspect)
        {
            cropWidth = (int)Math.Round(sourceHeight * targetAspect);
            cropHeight = sourceHeight;
        }
        else if (sourceAspect < targetAspect)
        {
            cropWidth = sourceWidth;
            cropHeight = (int)Math.Round(sourceWidth / targetAspect);
        }

        cropWidth = Math.Min(cropWidth, sourceWidth);
        cropHeight = Math.Min(cropHeight, sourceHeight);
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            return null;
        }

        var offsetX = Math.Max(0, (sourceWidth - cropWidth) / 2);
        var offsetY = Math.Max(0, (sourceHeight - cropHeight) / 2);
        var rect = new Int32Rect(offsetX, offsetY, cropWidth, cropHeight);

        try
        {
            var cropped = new CroppedBitmap(source, rect);
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return null;
        }
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

    public string CountdownText => _countdownSecondsRemaining > 0 ? _countdownSecondsRemaining.ToString() : string.Empty;

    public bool IsCountdownActive => _countdownSecondsRemaining > 0;

    private void SetCountdown(int remainingSeconds)
    {
        if (_countdownSecondsRemaining == remainingSeconds)
        {
            return;
        }

        _countdownSecondsRemaining = remainingSeconds;
        OnPropertyChanged(nameof(CountdownText));
        OnPropertyChanged(nameof(IsCountdownActive));
    }

    private int GetCameraTimerSeconds()
    {
        var seconds = _settings.CameraTimerSeconds;
        if (seconds < 0)
        {
            return 0;
        }

        if (seconds > MaxCountdownSeconds)
        {
            return MaxCountdownSeconds;
        }

        return seconds == 0 ? 0 : seconds;
    }
}
