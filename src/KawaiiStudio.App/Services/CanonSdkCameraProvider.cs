using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EDSDKLib;

namespace KawaiiStudio.App.Services;

public sealed class CanonSdkCameraProvider : ICameraProvider
{
    private const int CaptureTimeoutSeconds = 15;
    private const int RetryDelayMilliseconds = 200;
    private const int MaxRetryAttempts = 5;
    private const int MaxDownloadAttempts = 50;
    private const uint DefaultSaveTo = (uint)EDSDK.EdsSaveTo.Host;

    private readonly object _sync = new();
    private readonly ManualResetEventSlim _sdkReady = new(false);
    private Dispatcher? _sdkDispatcher;
    private Thread? _sdkThread;
    private IntPtr _cameraListRef = IntPtr.Zero;
    private IntPtr _cameraRef = IntPtr.Zero;
    private bool _initialized;
    private GCHandle _contextHandle;
    private EDSDK.EdsObjectEventHandler? _objectHandler;
    private TaskCompletionSource<bool>? _captureTcs;
    private string? _pendingCapturePath;
    private bool _liveViewActive;
    private bool _captureInProgress;
    private uint _preferredSaveTo = DefaultSaveTo;
    private string? _lastDownloadedName;
    private uint _lastDownloadedDateTime;

    public bool IsConnected { get; private set; }

    public CanonSdkCameraProvider()
    {
        StartSdkThread();
    }

    public Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return Task.FromResult(true);
        }

        return Task.Run(() => ConnectInternal(), cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => DisconnectInternal(), cancellationToken);
    }

    public Task<bool> StartLiveViewAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            return Task.FromResult(false);
        }

        return Task.Run(StartLiveViewInternal, cancellationToken);
    }

    public Task StopLiveViewAsync(CancellationToken cancellationToken)
    {
        return Task.Run(StopLiveViewInternal, cancellationToken);
    }

    public Task<BitmapSource?> GetLiveViewFrameAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected || !_liveViewActive || _captureInProgress)
        {
            return Task.FromResult<BitmapSource?>(null);
        }

        return Task.Run(DownloadLiveViewFrame, cancellationToken);
    }

    public async Task<bool> CapturePhotoAsync(string outputPath, CancellationToken cancellationToken)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(outputPath))
        {
            return false;
        }

        try
        {
            _captureInProgress = true;
            TaskCompletionSource<bool> tcs;
            lock (_sync)
            {
                if (_captureTcs is not null)
                {
                    return false;
                }

                _pendingCapturePath = outputPath;
                _captureTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                tcs = _captureTcs;
            }

            var prep = PrepareForCapture();
            if (prep != EDSDK.EDS_ERR_OK)
            {
                KawaiiStudio.App.App.Log($"CAPTURE_SDK_PREP_FAILED code=0x{prep:X8}");
                ClearPendingCapture(false);
                return false;
            }

            var result = ExecuteWithRetry(() =>
            {
                var err = EDSDK.EdsSendCommand(
                    _cameraRef,
                    EDSDK.CameraCommand_TakePicture,
                    0);

                if (err == EDSDK.EDS_ERR_OK)
                {
                    return err;
                }

                if (err != EDSDK.EDS_ERR_NOT_SUPPORTED && err != EDSDK.EDS_ERR_INVALID_PARAMETER)
                {
                    return err;
                }

                err = EDSDK.EdsSendCommand(
                    _cameraRef,
                    EDSDK.CameraCommand_PressShutterButton,
                    (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely);

                if (err != EDSDK.EDS_ERR_OK)
                {
                    return err;
                }

                return EDSDK.EdsSendCommand(
                    _cameraRef,
                    EDSDK.CameraCommand_PressShutterButton,
                    (int)EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF);
            });
            if (result != EDSDK.EDS_ERR_OK)
            {
                KawaiiStudio.App.App.Log($"CAPTURE_SDK_COMMAND_FAILED code=0x{result:X8}");
                ClearPendingCapture(false);
                return false;
            }

            using var registration = cancellationToken.Register(() => ClearPendingCapture(false));
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(CaptureTimeoutSeconds), cancellationToken));
            if (completed != tcs.Task)
            {
                KawaiiStudio.App.App.Log("CAPTURE_SDK_TIMEOUT");
                var fallback = TryDownloadLatestFromCamera(outputPath);
                if (fallback)
                {
                    ClearPendingCapture(true);
                    return true;
                }

                SwitchSaveToOnFailure();
                ClearPendingCapture(false);
                return false;
            }

            return await tcs.Task;
        }
        finally
        {
            _captureInProgress = false;
            if (_liveViewActive)
            {
                EnsureLiveViewOutput();
            }
        }
    }

    public Task<bool> StartVideoAsync(string outputPath, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<bool> StopVideoAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    private bool ConnectInternal()
    {
        if (_initialized)
        {
            IsConnected = true;
            return true;
        }

        try
        {
            var err = ExecuteSdk(() => EDSDK.EdsInitializeSDK());
            if (err != EDSDK.EDS_ERR_OK)
            {
                return false;
            }

            _initialized = true;

            err = ExecuteSdk(() => EDSDK.EdsGetCameraList(out _cameraListRef));
            if (err != EDSDK.EDS_ERR_OK)
            {
                DisconnectInternal();
                return false;
            }

            var cameraCount = 0;
            err = ExecuteSdk(() => EDSDK.EdsGetChildCount(_cameraListRef, out cameraCount));
            if (err != EDSDK.EDS_ERR_OK || cameraCount == 0)
            {
                DisconnectInternal();
                return false;
            }

            err = ExecuteSdk(() => EDSDK.EdsGetChildAtIndex(_cameraListRef, 0, out _cameraRef));
            if (err != EDSDK.EDS_ERR_OK || _cameraRef == IntPtr.Zero)
            {
                DisconnectInternal();
                return false;
            }

            err = ExecuteSdk(() => EDSDK.EdsOpenSession(_cameraRef));
            if (err != EDSDK.EDS_ERR_OK)
            {
                DisconnectInternal();
                return false;
            }

            _objectHandler = HandleObjectEvent;
            _contextHandle = GCHandle.Alloc(this);
            err = ExecuteSdk(() => EDSDK.EdsSetObjectEventHandler(
                _cameraRef,
                EDSDK.ObjectEvent_All,
                _objectHandler,
                GCHandle.ToIntPtr(_contextHandle)));

            if (err != EDSDK.EDS_ERR_OK)
            {
                DisconnectInternal();
                return false;
            }

            var saveTo = (uint)EDSDK.EdsSaveTo.Host;
            err = SetUIntProperty(EDSDK.PropID_SaveTo, saveTo);
            if (err != EDSDK.EDS_ERR_OK)
            {
                KawaiiStudio.App.App.Log($"CAMERA_SAVE_TO_INIT_FAILED code=0x{err:X8}");
            }
            else
            {
                var capacity = new EDSDK.EdsCapacity
                {
                    NumberOfFreeClusters = 0x7FFFFFFF,
                    BytesPerSector = 0x1000,
                    Reset = 1
                };
                err = ExecuteWithRetry(() => EDSDK.EdsSetCapacity(_cameraRef, capacity));
                if (err != EDSDK.EDS_ERR_OK)
                {
                    KawaiiStudio.App.App.Log($"CAMERA_CAPACITY_INIT_FAILED code=0x{err:X8}");
                }
            }

            IsConnected = true;
            return true;
        }
        catch (DllNotFoundException)
        {
            DisconnectInternal();
            return false;
        }
        catch (BadImageFormatException)
        {
            DisconnectInternal();
            return false;
        }
    }

    private void DisconnectInternal()
    {
        if (_cameraRef != IntPtr.Zero)
        {
            ExecuteSdk(() => EDSDK.EdsCloseSession(_cameraRef));
            ExecuteSdk(() => EDSDK.EdsRelease(_cameraRef));
            _cameraRef = IntPtr.Zero;
        }

        if (_cameraListRef != IntPtr.Zero)
        {
            ExecuteSdk(() => EDSDK.EdsRelease(_cameraListRef));
            _cameraListRef = IntPtr.Zero;
        }

        if (_contextHandle.IsAllocated)
        {
            _contextHandle.Free();
        }

        if (_initialized)
        {
            ExecuteSdk(() => EDSDK.EdsTerminateSDK());
            _initialized = false;
        }

        IsConnected = false;
        _liveViewActive = false;
    }

    private uint HandleObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
    {
        if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer
            || inEvent == EDSDK.ObjectEvent_DirItemRequestTransferDT
            || inEvent == EDSDK.ObjectEvent_DirItemCreated)
        {
            KawaiiStudio.App.App.Log($"CAPTURE_SDK_EVENT event=0x{inEvent:X8}");
            if (inRef == IntPtr.Zero)
            {
                CompletePendingCapture(false);
                return EDSDK.EDS_ERR_OK;
            }

            ExecuteSdk(() => EDSDK.EdsRetain(inRef));
            _ = Task.Run(() =>
            {
                var success = TryDownload(inRef);
                CompletePendingCapture(success);
            });
            return EDSDK.EDS_ERR_OK;
        }

        if (inRef != IntPtr.Zero)
        {
            ExecuteSdk(() => EDSDK.EdsRelease(inRef));
        }

        return EDSDK.EDS_ERR_OK;
    }

    private bool TryDownload(IntPtr directoryItem)
    {
        string? outputPath;
        lock (_sync)
        {
            outputPath = _pendingCapturePath;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            if (directoryItem != IntPtr.Zero)
            {
                ExecuteSdk(() => EDSDK.EdsDownloadCancel(directoryItem));
                ExecuteSdk(() => EDSDK.EdsRelease(directoryItem));
            }

            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        EDSDK.EdsDirectoryItemInfo info = default;
        var err = ExecuteSdk(() => EDSDK.EdsGetDirectoryItemInfo(directoryItem, out info));
        if (err != EDSDK.EDS_ERR_OK)
        {
            KawaiiStudio.App.App.Log($"CAPTURE_SDK_INFO_FAILED code=0x{err:X8}");
            ExecuteSdk(() => EDSDK.EdsDownloadCancel(directoryItem));
            ExecuteSdk(() => EDSDK.EdsRelease(directoryItem));
            return false;
        }

        var targetPath = ApplyExtension(outputPath, info.szFileName);
        var success = DownloadDirectoryItem(directoryItem, info, targetPath);

        if (directoryItem != IntPtr.Zero)
        {
            ExecuteSdk(() => EDSDK.EdsRelease(directoryItem));
        }

        return success;
    }

    private bool DownloadDirectoryItem(IntPtr directoryItem, EDSDK.EdsDirectoryItemInfo info, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        IntPtr stream = IntPtr.Zero;
        var err = ExecuteSdk(() => EDSDK.EdsCreateFileStream(
            outputPath,
            EDSDK.EdsFileCreateDisposition.CreateAlways,
            EDSDK.EdsAccess.ReadWrite,
            out stream));

        if (err != EDSDK.EDS_ERR_OK)
        {
            KawaiiStudio.App.App.Log($"CAPTURE_SDK_STREAM_FAILED code=0x{err:X8}");
            ExecuteSdk(() => EDSDK.EdsDownloadCancel(directoryItem));
        }
        else
        {
            err = DownloadWithRetry(directoryItem, info.Size, stream);
            if (err != EDSDK.EDS_ERR_OK)
            {
                KawaiiStudio.App.App.Log($"CAPTURE_SDK_DOWNLOAD_FAILED code=0x{err:X8} size={info.Size}");
                ExecuteSdk(() => EDSDK.EdsDownloadCancel(directoryItem));
            }
            else
            {
                err = ExecuteSdk(() => EDSDK.EdsDownloadComplete(directoryItem));
                if (err != EDSDK.EDS_ERR_OK)
                {
                    KawaiiStudio.App.App.Log($"CAPTURE_SDK_COMPLETE_FAILED code=0x{err:X8}");
                }
            }
        }

        if (stream != IntPtr.Zero)
        {
            ExecuteSdk(() => EDSDK.EdsRelease(stream));
        }

        var success = err == EDSDK.EDS_ERR_OK;
        if (success)
        {
            _lastDownloadedName = info.szFileName;
            _lastDownloadedDateTime = info.dateTime;
            KawaiiStudio.App.App.Log($"CAPTURE_SDK_DOWNLOAD file={info.szFileName} size={info.Size}");

            if (_preferredSaveTo != DefaultSaveTo)
            {
                var deleteResult = ExecuteSdk(() => EDSDK.EdsDeleteDirectoryItem(directoryItem));
                if (deleteResult != EDSDK.EDS_ERR_OK)
                {
                    KawaiiStudio.App.App.Log($"CAPTURE_SDK_DELETE_FAILED code=0x{deleteResult:X8}");
                }
            }
        }

        return success;
    }

    private bool TryDownloadLatestFromCamera(string outputPath)
    {
        if (_cameraRef == IntPtr.Zero)
        {
            return false;
        }

        KawaiiStudio.App.App.Log("CAPTURE_SDK_FALLBACK_START");

        var volumeCount = 0;
        var err = ExecuteSdk(() => EDSDK.EdsGetChildCount(_cameraRef, out volumeCount));
        if (err != EDSDK.EDS_ERR_OK || volumeCount == 0)
        {
            KawaiiStudio.App.App.Log($"CAPTURE_SDK_FALLBACK_NO_VOLUMES code=0x{err:X8}");
            return false;
        }

        IntPtr latestItem = IntPtr.Zero;
        EDSDK.EdsDirectoryItemInfo latestInfo = default;
        var latestDate = 0U;

        for (var volumeIndex = 0; volumeIndex < volumeCount; volumeIndex++)
        {
            IntPtr volume = IntPtr.Zero;
            err = ExecuteSdk(() => EDSDK.EdsGetChildAtIndex(_cameraRef, volumeIndex, out volume));
            if (err != EDSDK.EDS_ERR_OK || volume == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                UpdateLatestFromVolume(volume, ref latestItem, ref latestInfo, ref latestDate);
            }
            finally
            {
                ExecuteSdk(() => EDSDK.EdsRelease(volume));
            }
        }

        if (latestItem == IntPtr.Zero)
        {
            KawaiiStudio.App.App.Log("CAPTURE_SDK_FALLBACK_EMPTY");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_lastDownloadedName)
            && string.Equals(_lastDownloadedName, latestInfo.szFileName, StringComparison.OrdinalIgnoreCase)
            && _lastDownloadedDateTime == latestInfo.dateTime)
        {
            KawaiiStudio.App.App.Log($"CAPTURE_SDK_FALLBACK_DUPLICATE file={latestInfo.szFileName}");
            ExecuteSdk(() => EDSDK.EdsRelease(latestItem));
            return false;
        }

        var targetPath = ApplyExtension(outputPath, latestInfo.szFileName);
        var success = DownloadDirectoryItem(latestItem, latestInfo, targetPath);
        var itemToRelease = latestItem;
        ExecuteSdk(() => EDSDK.EdsRelease(itemToRelease));
        if (success)
        {
            KawaiiStudio.App.App.Log($"CAPTURE_SDK_FALLBACK_OK file={latestInfo.szFileName}");
        }

        return success;
    }

    private void UpdateLatestFromVolume(
        IntPtr volume,
        ref IntPtr latestItem,
        ref EDSDK.EdsDirectoryItemInfo latestInfo,
        ref uint latestDate)
    {
        var itemCount = 0;
        var err = ExecuteSdk(() => EDSDK.EdsGetChildCount(volume, out itemCount));
        if (err != EDSDK.EDS_ERR_OK || itemCount == 0)
        {
            return;
        }

        for (var index = 0; index < itemCount; index++)
        {
            IntPtr item = IntPtr.Zero;
            err = ExecuteSdk(() => EDSDK.EdsGetChildAtIndex(volume, index, out item));
            if (err != EDSDK.EDS_ERR_OK || item == IntPtr.Zero)
            {
                continue;
            }

            EDSDK.EdsDirectoryItemInfo info = default;
            err = ExecuteSdk(() => EDSDK.EdsGetDirectoryItemInfo(item, out info));
            if (err == EDSDK.EDS_ERR_OK && info.isFolder == 1 && string.Equals(info.szFileName, "DCIM", StringComparison.OrdinalIgnoreCase))
            {
                UpdateLatestFromDcim(item, ref latestItem, ref latestInfo, ref latestDate);
                ExecuteSdk(() => EDSDK.EdsRelease(item));
                return;
            }

            ExecuteSdk(() => EDSDK.EdsRelease(item));
        }
    }

    private void UpdateLatestFromDcim(
        IntPtr dcimFolder,
        ref IntPtr latestItem,
        ref EDSDK.EdsDirectoryItemInfo latestInfo,
        ref uint latestDate)
    {
        var folderCount = 0;
        var err = ExecuteSdk(() => EDSDK.EdsGetChildCount(dcimFolder, out folderCount));
        if (err != EDSDK.EDS_ERR_OK || folderCount == 0)
        {
            return;
        }

        for (var folderIndex = 0; folderIndex < folderCount; folderIndex++)
        {
            IntPtr folder = IntPtr.Zero;
            err = ExecuteSdk(() => EDSDK.EdsGetChildAtIndex(dcimFolder, folderIndex, out folder));
            if (err != EDSDK.EDS_ERR_OK || folder == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                EDSDK.EdsDirectoryItemInfo folderInfo = default;
                err = ExecuteSdk(() => EDSDK.EdsGetDirectoryItemInfo(folder, out folderInfo));
                if (err != EDSDK.EDS_ERR_OK || folderInfo.isFolder == 0)
                {
                    continue;
                }

                UpdateLatestFromFolder(folder, ref latestItem, ref latestInfo, ref latestDate);
            }
            finally
            {
                ExecuteSdk(() => EDSDK.EdsRelease(folder));
            }
        }
    }

    private void UpdateLatestFromFolder(
        IntPtr folder,
        ref IntPtr latestItem,
        ref EDSDK.EdsDirectoryItemInfo latestInfo,
        ref uint latestDate)
    {
        var fileCount = 0;
        var err = ExecuteSdk(() => EDSDK.EdsGetChildCount(folder, out fileCount));
        if (err != EDSDK.EDS_ERR_OK || fileCount == 0)
        {
            return;
        }

        for (var fileIndex = 0; fileIndex < fileCount; fileIndex++)
        {
            IntPtr fileItem = IntPtr.Zero;
            err = ExecuteSdk(() => EDSDK.EdsGetChildAtIndex(folder, fileIndex, out fileItem));
            if (err != EDSDK.EDS_ERR_OK || fileItem == IntPtr.Zero)
            {
                continue;
            }

            EDSDK.EdsDirectoryItemInfo fileInfo = default;
            err = ExecuteSdk(() => EDSDK.EdsGetDirectoryItemInfo(fileItem, out fileInfo));
            if (err == EDSDK.EDS_ERR_OK && fileInfo.isFolder == 0)
            {
                if (IsNewerCandidate(fileInfo, latestInfo, latestDate))
                {
                    if (latestItem != IntPtr.Zero)
                    {
                        var itemToRelease = latestItem;
                        ExecuteSdk(() => EDSDK.EdsRelease(itemToRelease));
                    }

                    ExecuteSdk(() => EDSDK.EdsRetain(fileItem));
                    latestItem = fileItem;
                    latestInfo = fileInfo;
                    latestDate = fileInfo.dateTime;
                }
            }

            ExecuteSdk(() => EDSDK.EdsRelease(fileItem));
        }
    }

    private static bool IsNewerCandidate(EDSDK.EdsDirectoryItemInfo candidate, EDSDK.EdsDirectoryItemInfo current, uint currentDate)
    {
        if (candidate.dateTime > currentDate)
        {
            return true;
        }

        if (candidate.dateTime == 0 && currentDate == 0)
        {
            return string.Compare(candidate.szFileName, current.szFileName, StringComparison.OrdinalIgnoreCase) > 0;
        }

        return false;
    }

    private static string ApplyExtension(string outputPath, string? cameraFileName)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return outputPath;
        }

        if (string.IsNullOrWhiteSpace(cameraFileName))
        {
            return outputPath;
        }

        var extension = Path.GetExtension(cameraFileName);
        return string.IsNullOrWhiteSpace(extension) ? outputPath : Path.ChangeExtension(outputPath, extension);
    }

    private uint DownloadWithRetry(IntPtr directoryItem, ulong size, IntPtr stream)
    {
        uint err = EDSDK.EDS_ERR_OK;
        for (var attempt = 0; attempt < MaxDownloadAttempts; attempt++)
        {
            err = ExecuteSdk(() => EDSDK.EdsDownload(directoryItem, size, stream));
            if (err != EDSDK.EDS_ERR_OBJECT_NOTREADY && err != EDSDK.EDS_ERR_DEVICE_BUSY)
            {
                return err;
            }

            Thread.Sleep(RetryDelayMilliseconds);
        }

        return err;
    }

    private void CompletePendingCapture(bool success)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_sync)
        {
            tcs = _captureTcs;
            _captureTcs = null;
            _pendingCapturePath = null;
        }

        tcs?.TrySetResult(success);
    }

    private void ClearPendingCapture(bool success)
    {
        TaskCompletionSource<bool>? tcs;
        lock (_sync)
        {
            tcs = _captureTcs;
            _captureTcs = null;
            _pendingCapturePath = null;
        }

        tcs?.TrySetResult(success);
    }

    private bool StartLiveViewInternal()
    {
        if (_cameraRef == IntPtr.Zero)
        {
            return false;
        }

        var evfMode = GetUIntProperty(EDSDK.PropID_Evf_Mode, 0);
        if (evfMode == 0)
        {
            var err = SetUIntProperty(EDSDK.PropID_Evf_Mode, 1U);
            if (err != EDSDK.EDS_ERR_OK)
            {
                return false;
            }
        }

        var device = GetUIntProperty(EDSDK.PropID_Evf_OutputDevice, 0);
        if ((device & EDSDK.EvfOutputDevice_PC) == 0)
        {
            device |= EDSDK.EvfOutputDevice_PC;
            var err = SetUIntProperty(EDSDK.PropID_Evf_OutputDevice, device);
            if (err != EDSDK.EDS_ERR_OK)
            {
                return false;
            }
        }

        _liveViewActive = true;
        return true;
    }

    private void EnsureLiveViewOutput()
    {
        if (_cameraRef == IntPtr.Zero)
        {
            return;
        }

        var evfMode = GetUIntProperty(EDSDK.PropID_Evf_Mode, 0);
        if (evfMode == 0)
        {
            SetUIntProperty(EDSDK.PropID_Evf_Mode, 1U);
        }

        var device = GetUIntProperty(EDSDK.PropID_Evf_OutputDevice, 0);
        if ((device & EDSDK.EvfOutputDevice_PC) == 0)
        {
            device |= EDSDK.EvfOutputDevice_PC;
            SetUIntProperty(EDSDK.PropID_Evf_OutputDevice, device);
        }
    }

    private void StopLiveViewInternal()
    {
        if (_cameraRef == IntPtr.Zero)
        {
            return;
        }

        var device = GetUIntProperty(EDSDK.PropID_Evf_OutputDevice, 0);
        if ((device & EDSDK.EvfOutputDevice_PC) != 0)
        {
            device &= ~EDSDK.EvfOutputDevice_PC;
            SetUIntProperty(EDSDK.PropID_Evf_OutputDevice, device);
        }

        _liveViewActive = false;
    }

    private BitmapSource? DownloadLiveViewFrame()
    {
        IntPtr stream = IntPtr.Zero;
        IntPtr evfImage = IntPtr.Zero;
        try
        {
            var err = ExecuteWithRetry(() => EDSDK.EdsCreateMemoryStream(2 * 1024 * 1024, out stream));
            if (err != EDSDK.EDS_ERR_OK)
            {
                return null;
            }

            err = ExecuteWithRetry(() => EDSDK.EdsCreateEvfImageRef(stream, out evfImage));
            if (err != EDSDK.EDS_ERR_OK)
            {
                return null;
            }

            err = ExecuteWithRetry(() => EDSDK.EdsDownloadEvfImage(_cameraRef, evfImage));
            if (err == EDSDK.EDS_ERR_OBJECT_NOTREADY || err == EDSDK.EDS_ERR_DEVICE_BUSY)
            {
                return null;
            }

            if (err != EDSDK.EDS_ERR_OK)
            {
                return null;
            }

            var pointer = IntPtr.Zero;
            err = ExecuteSdk(() => EDSDK.EdsGetPointer(stream, out pointer));
            if (err != EDSDK.EDS_ERR_OK)
            {
                return null;
            }

            ulong length = 0;
            err = ExecuteSdk(() => EDSDK.EdsGetLength(stream, out length));
            if (err != EDSDK.EDS_ERR_OK || length == 0 || length > int.MaxValue)
            {
                return null;
            }

            var bytes = new byte[(int)length];
            Marshal.Copy(pointer, bytes, 0, (int)length);
            return LoadBitmap(bytes);
        }
        finally
        {
            if (evfImage != IntPtr.Zero)
            {
                ExecuteSdk(() => EDSDK.EdsRelease(evfImage));
            }

            if (stream != IntPtr.Zero)
            {
                ExecuteSdk(() => EDSDK.EdsRelease(stream));
            }
        }
    }

    private static BitmapSource? LoadBitmap(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private uint GetUIntProperty(uint propertyId, uint fallback)
    {
        if (_cameraRef == IntPtr.Zero)
        {
            return fallback;
        }

        uint value = 0;
        var err = ExecuteSdk(() => EDSDK.EdsGetPropertyData(_cameraRef, propertyId, 0, out value));
        return err == EDSDK.EDS_ERR_OK ? value : fallback;
    }

    private uint SetUIntProperty(uint propertyId, uint value)
    {
        if (_cameraRef == IntPtr.Zero)
        {
            return EDSDK.EDS_ERR_INTERNAL_ERROR;
        }

        var size = GetPropertySize(propertyId);
        return ExecuteWithRetry(() => EDSDK.EdsSetPropertyData(_cameraRef, propertyId, 0, size, value));
    }

    private int GetPropertySize(uint propertyId)
    {
        if (_cameraRef == IntPtr.Zero)
        {
            return sizeof(uint);
        }

        EDSDK.EdsDataType dataType = 0;
        var size = 0;
        var err = ExecuteSdk(() => EDSDK.EdsGetPropertySize(_cameraRef, propertyId, 0, out dataType, out size));
        return err == EDSDK.EDS_ERR_OK && size > 0 ? size : sizeof(uint);
    }

    private uint PrepareForCapture()
    {
        if (_cameraRef == IntPtr.Zero)
        {
            return EDSDK.EDS_ERR_INTERNAL_ERROR;
        }

        var fixedMovie = GetUIntProperty(EDSDK.PropID_FixedMovie, 0);
        if (fixedMovie != 0)
        {
            KawaiiStudio.App.App.Log($"CAPTURE_SDK_FIXED_MOVIE value={fixedMovie}");
        }

        var saveTo = _preferredSaveTo;
        var err = SetUIntProperty(EDSDK.PropID_SaveTo, saveTo);
        if (err != EDSDK.EDS_ERR_OK)
        {
            KawaiiStudio.App.App.Log($"CAPTURE_SDK_SAVE_TO_FAILED code=0x{err:X8}");
            return err;
        }

        KawaiiStudio.App.App.Log($"CAPTURE_SDK_SAVE_TO value={saveTo}");
        if (saveTo != (uint)EDSDK.EdsSaveTo.Camera)
        {
            var capacity = new EDSDK.EdsCapacity
            {
                NumberOfFreeClusters = 0x7FFFFFFF,
                BytesPerSector = 0x1000,
                Reset = 1
            };

            err = ExecuteWithRetry(() => EDSDK.EdsSetCapacity(_cameraRef, capacity));
        }

        return err;
    }

    private void SwitchSaveToOnFailure()
    {
        if (_preferredSaveTo != DefaultSaveTo)
        {
            return;
        }

        _preferredSaveTo = (uint)EDSDK.EdsSaveTo.Both;
        KawaiiStudio.App.App.Log($"CAPTURE_SDK_SAVE_TO_SWITCH value={_preferredSaveTo}");
    }

    private uint ExecuteSdk(Func<uint> action)
    {
        EnsureSdkReady();
        if (_sdkDispatcher is null || _sdkDispatcher.CheckAccess())
        {
            return action();
        }

        return _sdkDispatcher.Invoke(action);
    }

    private void StartSdkThread()
    {
        if (_sdkThread is not null)
        {
            return;
        }

        _sdkThread = new Thread(() =>
        {
            _sdkDispatcher = Dispatcher.CurrentDispatcher;
            _sdkReady.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "CanonSdkThread"
        };

        _sdkThread.SetApartmentState(ApartmentState.STA);
        _sdkThread.Start();
        _sdkReady.Wait();
    }

    private void EnsureSdkReady()
    {
        if (!_sdkReady.IsSet)
        {
            _sdkReady.Wait();
        }
    }

    private uint ExecuteWithRetry(Func<uint> action)
    {
        uint err = EDSDK.EDS_ERR_OK;
        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            err = ExecuteSdk(action);
            if (err != EDSDK.EDS_ERR_DEVICE_BUSY)
            {
                return err;
            }

            Thread.Sleep(RetryDelayMilliseconds);
        }

        return err;
    }
}
