using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class FinalizeViewModel : ScreenViewModelBase
{
    private const int ExpectedShotCount = 8;
    private const int AutoNavigateDelayMilliseconds = 1000;
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly FrameCompositionService _composer;
    private readonly VideoCompilationService _videoCompiler;
    private readonly UploadService _uploadService;
    private CancellationTokenSource? _cts;
    private string _statusText = "Preparing output...";

    public FinalizeViewModel(
        NavigationService navigation,
        SessionService session,
        FrameCompositionService composer,
        VideoCompilationService videoCompiler,
        UploadService uploadService,
        ThemeCatalogService themeCatalog)
        : base(themeCatalog, "finalize")
    {
        _navigation = navigation;
        _session = session;
        _composer = composer;
        _videoCompiler = videoCompiler;
        _uploadService = uploadService;
    }

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
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var outputPaths = BuildOutputPaths();
        if (outputPaths is null)
        {
            StatusText = "Session output folder missing.";
            KawaiiStudio.App.App.Log("FINALIZE_COMPOSITE_FAILED reason=missing_session_folder");
            // Auto-navigate even on failure
            await AutoNavigateAfterDelay();
            return;
        }

        var (withQrPath, withoutQrPath) = outputPaths.Value;
        if (_composer.TrySavePrintComposite(_session.Current, withoutQrPath, includeQr: false, out var error))
        {
            var videoOk = false;
            if (!CanBuildVideo(out var skipReason))
            {
                StatusText = "Composite ready. Video skipped.";
                KawaiiStudio.App.App.Log($"FINALIZE_VIDEO_SKIPPED reason={skipReason}");
            }
            else
            {
                StatusText = "Composite ready. Rendering video...";
                var videoResult = await Task.Run(() =>
                    _videoCompiler.TryBuildPreviewVideo(_session.Current, out var videoPath, out var videoError)
                        ? (success: true, path: videoPath, error: (string?)null)
                        : (success: false, path: (string?)null, error: videoError));

                if (videoResult.success && !string.IsNullOrWhiteSpace(videoResult.path))
                {
                    _session.SetVideoPath(videoResult.path);
                    videoOk = true;
                    KawaiiStudio.App.App.Log($"FINALIZE_VIDEO_OK file={Path.GetFileName(videoResult.path)}");
                }
                else
                {
                    var reason = string.IsNullOrWhiteSpace(videoResult.error) ? "unknown" : videoResult.error;
                    _session.SetVideoPath(null);
                    KawaiiStudio.App.App.Log($"FINALIZE_VIDEO_FAILED reason={reason}");
                }
            }

            var uploadOk = await TryUploadAsync(withoutQrPath);
            _session.SetFinalImagePath(withQrPath);
            if (_composer.TrySavePrintComposite(_session.Current, withQrPath, out var qrError))
            {
                if (videoOk && uploadOk)
                {
                    StatusText = "Composite, video, and upload ready.";
                }
                else if (videoOk && _uploadService.IsEnabled && !uploadOk)
                {
                    StatusText = "Composite and video ready. Upload failed - printing without QR.";
                }
                else if (videoOk)
                {
                    StatusText = "Composite and video ready.";
                }
                else if (_uploadService.IsEnabled && !uploadOk)
                {
                    StatusText = "Composite ready. Upload failed - printing without QR.";
                }
                else
                {
                    StatusText = "Composite ready. Video failed.";
                }
                KawaiiStudio.App.App.Log($"FINALIZE_COMPOSITE_OK file={Path.GetFileName(withQrPath)}");
            }
            else
            {
                StatusText = qrError ?? "Composite failed.";
                KawaiiStudio.App.App.Log($"FINALIZE_COMPOSITE_FAILED reason={qrError}");
            }

            // Auto-navigate after processing completes
            await AutoNavigateAfterDelay();
            return;
        }

        StatusText = error ?? "Composite failed.";
        KawaiiStudio.App.App.Log($"FINALIZE_COMPOSITE_FAILED reason={error}");
        // Auto-navigate even on failure
        await AutoNavigateAfterDelay();
    }

    private async Task AutoNavigateAfterDelay()
    {
        var token = _cts?.Token ?? CancellationToken.None;
        try
        {
            await Task.Delay(AutoNavigateDelayMilliseconds, token);
            if (!token.IsCancellationRequested)
            {
                _navigation.Navigate("printing");
            }
        }
        catch (TaskCanceledException)
        {
            // Ignore navigation cancellation.
        }
    }

    private (string withQr, string withoutQr)? BuildOutputPaths()
    {
        var sessionFolder = _session.Current.SessionFolder;
        if (string.IsNullOrWhiteSpace(sessionFolder))
        {
            return null;
        }

        var sessionId = string.IsNullOrWhiteSpace(_session.Current.SessionId) ? "session" : _session.Current.SessionId;
        var finalFolder = Path.Combine(sessionFolder, "Final");
        var withQr = Path.Combine(finalFolder, $"{sessionId}_final_qr.png");
        var withoutQr = Path.Combine(finalFolder, $"{sessionId}_final.png");
        return (withQr, withoutQr);
    }

    private bool CanBuildVideo(out string reason)
    {
        var capturedCount = _session.Current.CapturedPhotos.Count;
        if (capturedCount < ExpectedShotCount)
        {
            reason = $"captures_incomplete count={capturedCount}";
            return false;
        }

        var slotCount = _session.Current.SlotCount ?? 0;
        if (slotCount > 0 && _session.Current.SelectedMapping.Count < slotCount)
        {
            reason = "selection_incomplete";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private async Task<bool> TryUploadAsync(string imagePath)
    {
        if (!_uploadService.IsEnabled)
        {
            KawaiiStudio.App.App.Log("UPLOAD_SKIPPED reason=disabled");
            return false;
        }

        var videoPath = _session.Current.VideoPath;
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            KawaiiStudio.App.App.Log("UPLOAD_SKIPPED reason=video_missing");
            return false;
        }

        StatusText = "Uploading media...";
        KawaiiStudio.App.App.Log("UPLOAD_STARTED");
        var result = await _uploadService.UploadAsync(_session.Current, imagePath, videoPath, System.Threading.CancellationToken.None);
        if (result.ok && !string.IsNullOrWhiteSpace(result.url))
        {
            _session.SetQrUrl(result.url);
            KawaiiStudio.App.App.Log($"UPLOAD_OK url={result.url}");
            KawaiiStudio.App.App.Log($"QR_URL={result.url}");
            return true;
        }

        var failReason = string.IsNullOrWhiteSpace(result.error) ? "upload_failed" : result.error;
        KawaiiStudio.App.App.Log($"UPLOAD_FAILED reason={failReason}");
        return false;
    }
}
