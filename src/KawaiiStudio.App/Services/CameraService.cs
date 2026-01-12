using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace KawaiiStudio.App.Services;

public sealed class CameraService : ICameraProvider
{
    private ICameraProvider _provider;

    public CameraService(ICameraProvider provider)
    {
        _provider = provider;
    }

    public ICameraProvider Provider => _provider;

    public bool IsConnected => _provider.IsConnected;

    public void UseProvider(ICameraProvider provider)
    {
        _provider = provider;
    }

    public Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        return _provider.ConnectAsync(cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        return _provider.DisconnectAsync(cancellationToken);
    }

    public Task<bool> StartLiveViewAsync(CancellationToken cancellationToken)
    {
        return _provider.StartLiveViewAsync(cancellationToken);
    }

    public Task StopLiveViewAsync(CancellationToken cancellationToken)
    {
        return _provider.StopLiveViewAsync(cancellationToken);
    }

    public Task<BitmapSource?> GetLiveViewFrameAsync(CancellationToken cancellationToken)
    {
        return _provider.GetLiveViewFrameAsync(cancellationToken);
    }

    public Task<bool> CapturePhotoAsync(string outputPath, CancellationToken cancellationToken)
    {
        return _provider.CapturePhotoAsync(outputPath, cancellationToken);
    }

    public Task<bool> StartVideoAsync(string outputPath, CancellationToken cancellationToken)
    {
        return _provider.StartVideoAsync(outputPath, cancellationToken);
    }

    public Task<bool> StopVideoAsync(CancellationToken cancellationToken)
    {
        return _provider.StopVideoAsync(cancellationToken);
    }
}
