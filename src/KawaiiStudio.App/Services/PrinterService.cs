using System;
using System.Threading;
using System.Threading.Tasks;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class PrinterService
{
    private readonly IPrinterProvider _provider;

    public PrinterService(SettingsService settings, AppPaths appPaths)
    {
        _provider = new WindowsPrinterProvider(settings, appPaths.ConfigRoot);
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

        var quantity = session.Quantity ?? 0;
        if (quantity <= 0)
        {
            return Task.FromResult((false, (string?)null, "print_quantity_missing"));
        }

        var sheetCount = CalculateSheetCount(session.Size, quantity);
        if (sheetCount <= 0)
        {
            return Task.FromResult((false, (string?)null, "print_sheet_count_invalid"));
        }

        KawaiiStudio.App.App.Log($"PRINT_START sheets={sheetCount} size={session.Size} qty={quantity}");
        return _provider.PrintAsync(imagePath, sheetCount, session.Size, cancellationToken);
    }

    private static int CalculateSheetCount(PrintSize? size, int quantity)
    {
        if (quantity <= 0)
        {
            return 0;
        }

        return size switch
        {
            PrintSize.TwoBySix => Math.Max(1, quantity / 2),
            PrintSize.FourBySix => quantity,
            _ => 0
        };
    }
}
