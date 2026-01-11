using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public interface ICameraProvider
{
    bool IsConnected { get; }

    Task<bool> ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<bool> StartLiveViewAsync(CancellationToken cancellationToken);
    Task StopLiveViewAsync(CancellationToken cancellationToken);

    Task<bool> CapturePhotoAsync(string outputPath, CancellationToken cancellationToken);
    Task<bool> StartVideoAsync(string outputPath, CancellationToken cancellationToken);
    Task<bool> StopVideoAsync(CancellationToken cancellationToken);
}
