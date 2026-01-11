using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KawaiiStudio.App.Services;

public sealed class SimulatedCameraProvider : ICameraProvider
{
    public bool IsConnected { get; private set; }

    public Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<bool> StartLiveViewAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    public Task StopLiveViewAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<bool> CapturePhotoAsync(string outputPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Task.FromResult(false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        WritePlaceholderImage(outputPath, DateTime.Now);
        return Task.FromResult(true);
    }

    public Task<bool> StartVideoAsync(string outputPath, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    public Task<bool> StopVideoAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    private static void WritePlaceholderImage(string path, DateTime timestamp)
    {
        const int width = 800;
        const int height = 600;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 18)), null, new System.Windows.Rect(0, 0, width, height));

            var text = new FormattedText(
                $"Simulated Shot\n{timestamp:yyyy-MM-dd HH:mm:ss}",
                CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                32,
                Brushes.White,
                1.0);

            context.DrawText(text, new System.Windows.Point(32, 32));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }
}
