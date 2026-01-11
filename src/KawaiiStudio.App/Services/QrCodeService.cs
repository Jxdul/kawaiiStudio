using System;
using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace KawaiiStudio.App.Services;

public sealed class QrCodeService
{
    public BitmapSource? Render(string payload, int pixelsPerModule = 8)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(Math.Max(1, pixelsPerModule));

        return LoadBitmap(bytes);
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
}
