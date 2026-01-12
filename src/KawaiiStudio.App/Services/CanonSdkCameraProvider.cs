using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using EDSDKLib;

namespace KawaiiStudio.App.Services;

public sealed class CanonSdkCameraProvider : ICameraProvider
{
    private const int CaptureTimeoutSeconds = 8;
    private const int RetryDelayMilliseconds = 200;
    private const int MaxRetryAttempts = 5;

    private readonly object _sync = new();
    private IntPtr _cameraListRef = IntPtr.Zero;
    private IntPtr _cameraRef = IntPtr.Zero;
    private bool _initialized;
    private GCHandle _contextHandle;
    private EDSDK.EdsObjectEventHandler? _objectHandler;
    private TaskCompletionSource<bool>? _captureTcs;
    private string? _pendingCapturePath;
    private bool _liveViewActive;

    public bool IsConnected { get; private set; }

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
        if (!IsConnected || !_liveViewActive)
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

        var result = ExecuteWithRetry(() =>
        {
            var err = EDSDK.EdsSendCommand(
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
            ClearPendingCapture(false);
            return false;
        }

        using var registration = cancellationToken.Register(() => ClearPendingCapture(false));
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(CaptureTimeoutSeconds), cancellationToken));
        if (completed != tcs.Task)
        {
            ClearPendingCapture(false);
            return false;
        }

        return await tcs.Task;
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
            var err = EDSDK.EdsInitializeSDK();
            if (err != EDSDK.EDS_ERR_OK)
            {
                return false;
            }

            _initialized = true;

            err = EDSDK.EdsGetCameraList(out _cameraListRef);
            if (err != EDSDK.EDS_ERR_OK)
            {
                DisconnectInternal();
                return false;
            }

            err = EDSDK.EdsGetChildCount(_cameraListRef, out var cameraCount);
            if (err != EDSDK.EDS_ERR_OK || cameraCount == 0)
            {
                DisconnectInternal();
                return false;
            }

            err = EDSDK.EdsGetChildAtIndex(_cameraListRef, 0, out _cameraRef);
            if (err != EDSDK.EDS_ERR_OK || _cameraRef == IntPtr.Zero)
            {
                DisconnectInternal();
                return false;
            }

            err = EDSDK.EdsOpenSession(_cameraRef);
            if (err != EDSDK.EDS_ERR_OK)
            {
                DisconnectInternal();
                return false;
            }

            _objectHandler = HandleObjectEvent;
            _contextHandle = GCHandle.Alloc(this);
            err = EDSDK.EdsSetObjectEventHandler(
                _cameraRef,
                EDSDK.ObjectEvent_All,
                _objectHandler,
                GCHandle.ToIntPtr(_contextHandle));

            if (err != EDSDK.EDS_ERR_OK)
            {
                DisconnectInternal();
                return false;
            }

            var saveTo = (uint)EDSDK.EdsSaveTo.Host;
            err = ExecuteWithRetry(() =>
                EDSDK.EdsSetPropertyData(_cameraRef, EDSDK.PropID_SaveTo, 0, sizeof(uint), saveTo));
            if (err != EDSDK.EDS_ERR_OK)
            {
                DisconnectInternal();
                return false;
            }

            var capacity = new EDSDK.EdsCapacity
            {
                NumberOfFreeClusters = 0x7FFFFFFF,
                BytesPerSector = 0x1000,
                Reset = 1
            };
            err = ExecuteWithRetry(() => EDSDK.EdsSetCapacity(_cameraRef, capacity));
            if (err != EDSDK.EDS_ERR_OK)
            {
                DisconnectInternal();
                return false;
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
            EDSDK.EdsCloseSession(_cameraRef);
            EDSDK.EdsRelease(_cameraRef);
            _cameraRef = IntPtr.Zero;
        }

        if (_cameraListRef != IntPtr.Zero)
        {
            EDSDK.EdsRelease(_cameraListRef);
            _cameraListRef = IntPtr.Zero;
        }

        if (_contextHandle.IsAllocated)
        {
            _contextHandle.Free();
        }

        if (_initialized)
        {
            EDSDK.EdsTerminateSDK();
            _initialized = false;
        }

        IsConnected = false;
        _liveViewActive = false;
    }

    private uint HandleObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
    {
        if (inEvent == EDSDK.ObjectEvent_DirItemRequestTransfer
            || inEvent == EDSDK.ObjectEvent_DirItemCreated)
        {
            var success = TryDownload(inRef);
            CompletePendingCapture(success);
            return EDSDK.EDS_ERR_OK;
        }

        if (inRef != IntPtr.Zero)
        {
            EDSDK.EdsRelease(inRef);
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
                EDSDK.EdsRelease(directoryItem);
            }

            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        var err = EDSDK.EdsGetDirectoryItemInfo(directoryItem, out var info);
        if (err != EDSDK.EDS_ERR_OK)
        {
            EDSDK.EdsRelease(directoryItem);
            return false;
        }

        err = EDSDK.EdsCreateFileStream(
            outputPath,
            EDSDK.EdsFileCreateDisposition.CreateAlways,
            EDSDK.EdsAccess.ReadWrite,
            out var stream);

        if (err == EDSDK.EDS_ERR_OK)
        {
            err = DownloadWithRetry(directoryItem, info.Size, stream);
        }

        if (err == EDSDK.EDS_ERR_OK)
        {
            err = EDSDK.EdsDownloadComplete(directoryItem);
        }

        if (directoryItem != IntPtr.Zero)
        {
            EDSDK.EdsRelease(directoryItem);
        }

        if (stream != IntPtr.Zero)
        {
            EDSDK.EdsRelease(stream);
        }

        return err == EDSDK.EDS_ERR_OK;
    }

    private static uint DownloadWithRetry(IntPtr directoryItem, ulong size, IntPtr stream)
    {
        uint err = EDSDK.EDS_ERR_OK;
        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            err = EDSDK.EdsDownload(directoryItem, size, stream);
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
            var err = ExecuteWithRetry(() =>
                EDSDK.EdsSetPropertyData(_cameraRef, EDSDK.PropID_Evf_Mode, 0, sizeof(uint), 1U));
            if (err != EDSDK.EDS_ERR_OK)
            {
                return false;
            }
        }

        var device = GetUIntProperty(EDSDK.PropID_Evf_OutputDevice, 0);
        if ((device & EDSDK.EvfOutputDevice_PC) == 0)
        {
            device |= EDSDK.EvfOutputDevice_PC;
            var err = ExecuteWithRetry(() =>
                EDSDK.EdsSetPropertyData(_cameraRef, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), device));
            if (err != EDSDK.EDS_ERR_OK)
            {
                return false;
            }
        }

        _liveViewActive = true;
        return true;
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
            ExecuteWithRetry(() =>
                EDSDK.EdsSetPropertyData(_cameraRef, EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), device));
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

            err = EDSDK.EdsGetPointer(stream, out var pointer);
            if (err != EDSDK.EDS_ERR_OK)
            {
                return null;
            }

            err = EDSDK.EdsGetLength(stream, out var length);
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
                EDSDK.EdsRelease(evfImage);
            }

            if (stream != IntPtr.Zero)
            {
                EDSDK.EdsRelease(stream);
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

        var err = EDSDK.EdsGetPropertyData(_cameraRef, propertyId, 0, out uint value);
        return err == EDSDK.EDS_ERR_OK ? value : fallback;
    }

    private static uint ExecuteWithRetry(Func<uint> action)
    {
        uint err = EDSDK.EDS_ERR_OK;
        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            err = action();
            if (err != EDSDK.EDS_ERR_DEVICE_BUSY)
            {
                return err;
            }

            Thread.Sleep(RetryDelayMilliseconds);
        }

        return err;
    }
}
