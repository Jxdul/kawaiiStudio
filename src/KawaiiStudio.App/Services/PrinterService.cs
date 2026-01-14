using System;
using System.Threading;
using System.Threading.Tasks;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class PrinterService
{
    private readonly IPrinterProvider _provider;
    private readonly SettingsService _settings;

    public PrinterService(SettingsService settings, IPrinterProvider provider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task<(bool ok, string? jobId, string? error)> PrintAsync(
        SessionState session,
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return Task.FromResult((false, (string?)null, "print_session_missing"));
        }

        var imagePath = session.FinalImagePath;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return Task.FromResult((false, (string?)null, "print_image_missing"));
        }

        var size = session.Size;
        if (size is null)
        {
            return Task.FromResult((false, (string?)null, "print_size_missing"));
        }

        var quantity = session.Quantity ?? 0;
        if (quantity <= 0)
        {
            return Task.FromResult((false, (string?)null, "print_quantity_missing"));
        }

        var copyCount = CalculateCopyCount(size.Value, quantity);
        if (copyCount <= 0)
        {
            return Task.FromResult((false, (string?)null, "print_copy_count_invalid"));
        }

        KawaiiStudio.App.App.Log($"PRINT_START size={size} quantity={quantity} copies={copyCount}");
        return _provider.PrintAsync(imagePath, copyCount, size.Value, cancellationToken);
    }

    private static int CalculateCopyCount(PrintSize size, int quantity)
    {
        if (quantity <= 0)
        {
            return 0;
        }

        return size switch
        {
            PrintSize.TwoBySix => quantity / 2,  // 2 copies → 1, 4 copies → 2, 6 copies → 3
            PrintSize.FourBySix => quantity,     // 2 copies → 2, 4 copies → 4, 6 copies → 6
            _ => 0
        };
    }
}
