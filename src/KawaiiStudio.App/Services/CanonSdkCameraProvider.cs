using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EDSDKLib;

namespace KawaiiStudio.App.Services;

public sealed class CanonSdkCameraProvider : ICameraProvider
{
    private readonly object _sync = new();
    private IntPtr _cameraListRef = IntPtr.Zero;
    private IntPtr _cameraRef = IntPtr.Zero;
    private bool _initialized;
    private GCHandle _contextHandle;
    private EDSDK.EdsObjectEventHandler? _objectHandler;
    private TaskCompletionSource<bool>? _captureTcs;
    private string? _pendingCapturePath;

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
        return Task.FromResult(false);
    }

    public Task StopLiveViewAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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

        var result = EDSDK.EdsSendCommand(_cameraRef, EDSDK.CameraCommand_TakePicture, 0);
        if (result != EDSDK.EDS_ERR_OK)
        {
            ClearPendingCapture(false);
            return false;
        }

        using var registration = cancellationToken.Register(() => ClearPendingCapture(false));
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
            err = EDSDK.EdsSetPropertyData(_cameraRef, EDSDK.PropID_SaveTo, 0, sizeof(uint), saveTo);
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
            err = EDSDK.EdsSetCapacity(_cameraRef, capacity);
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
            err = EDSDK.EdsDownload(directoryItem, info.Size, stream);
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
}
