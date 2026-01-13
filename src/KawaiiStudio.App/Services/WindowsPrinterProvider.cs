using System;
using System.IO;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class WindowsPrinterProvider : IPrinterProvider
{
    private const double DefaultPageWidth = 4 * 96;  // 4 inches @ 96 DPI
    private const double DefaultPageHeight = 6 * 96; // 6 inches @ 96 DPI
    private const int TargetPrintResolution = 600;
    private const double TwoBySixScaleInset = 0.98;
    private readonly SettingsService _settings;
    private readonly string _configRoot;

    public WindowsPrinterProvider(SettingsService settings, string configRoot)
    {
        _settings = settings;
        _configRoot = string.IsNullOrWhiteSpace(configRoot) ? AppContext.BaseDirectory : configRoot;
    }

    public Task<(bool ok, string? jobId, string? error)> PrintAsync(
        string imagePath,
        int sheetCount,
        PrintSize? size,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return Task.FromResult((false, (string?)null, "print_image_missing"));
        }

        if (sheetCount <= 0)
        {
            return Task.FromResult((false, (string?)null, "print_sheet_count_invalid"));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return Task.FromResult((false, (string?)null, "print_dispatcher_missing"));
        }

        return dispatcher.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return (false, (string?)null, "print_canceled");
            }

            return PrintInternal(imagePath, sheetCount, size);
        }).Task;
    }

    private (bool ok, string? jobId, string? error) PrintInternal(string imagePath, int sheetCount, PrintSize? size)
    {
        var image = ImageCache.GetOrLoad(imagePath);
        if (image is null)
        {
            return (false, null, "print_image_load_failed");
        }

        var queue = ResolveQueue();
        if (queue is null)
        {
            return (false, null, "print_queue_missing");
        }

        var ticket = queue.DefaultPrintTicket ?? new PrintTicket();
        ticket.PageOrientation ??= PageOrientation.Portrait;
        ticket.PageMediaSize ??= new PageMediaSize(PageMediaSizeName.NorthAmerica4x6);
        ticket = ApplyTicketOverrides(queue, ticket, size);
        ticket = ApplyQualityOverrides(queue, ticket);
        if (size != PrintSize.TwoBySix)
        {
            ticket = NormalizeTicketForOrientation(queue, ticket);
        }

        var pageWidth = ticket.PageMediaSize.Width ?? DefaultPageWidth;
        var pageHeight = ticket.PageMediaSize.Height ?? DefaultPageHeight;
        if (size != PrintSize.TwoBySix)
        {
            (pageWidth, pageHeight) = NormalizePageSizeForOrientation(ticket.PageOrientation, pageWidth, pageHeight);
        }

        var isTwoBySix = size == PrintSize.TwoBySix;
        var rotationDegrees = isTwoBySix && pageWidth > pageHeight ? -90 : 0;
        var printImage = rotationDegrees == 0 ? image : RotateBitmap(image, rotationDegrees);
        var document = BuildDocument(printImage, sheetCount, pageWidth, pageHeight, fitToPage: isTwoBySix);
        var writer = PrintQueue.CreateXpsDocumentWriter(queue);
        writer.Write(document.DocumentPaginator, ticket);

        var jobId = $"print_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        return (true, jobId, null);
    }

    private PrintQueue? ResolveQueue()
    {
        var printerName = _settings.PrintName;
        try
        {
            if (!string.IsNullOrWhiteSpace(printerName))
            {
                return new PrintQueue(new PrintServer(), printerName);
            }
        }
        catch
        {
            // Fall back to default printer if named queue is missing.
        }

        try
        {
            return LocalPrintServer.GetDefaultPrintQueue();
        }
        catch
        {
            return null;
        }
    }

    private PrintTicket ApplyTicketOverrides(PrintQueue queue, PrintTicket baseTicket, PrintSize? size)
    {
        if (size != PrintSize.TwoBySix)
        {
            return baseTicket;
        }

        var ticketPath = ResolveTicketPath(_settings.TwoBySixPrintTicketPath);
        if (string.IsNullOrWhiteSpace(ticketPath) || !File.Exists(ticketPath))
        {
            return baseTicket;
        }

        try
        {
            using var stream = File.OpenRead(ticketPath);
            var overrideTicket = new PrintTicket(stream);
            var result = queue.MergeAndValidatePrintTicket(baseTicket, overrideTicket);
            if (result.ValidatedPrintTicket is not null)
            {
                KawaiiStudio.App.App.Log($"PRINT_TICKET_APPLIED path={ticketPath}");
                return result.ValidatedPrintTicket;
            }
        }
        catch (Exception ex)
        {
            var reason = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            KawaiiStudio.App.App.Log($"PRINT_TICKET_FAILED reason={reason}");
        }

        return baseTicket;
    }

    private PrintTicket ApplyQualityOverrides(PrintQueue queue, PrintTicket baseTicket)
    {
        var overrideTicket = new PrintTicket
        {
            PageResolution = new PageResolution(TargetPrintResolution, TargetPrintResolution)
        };

        try
        {
            var result = queue.MergeAndValidatePrintTicket(baseTicket, overrideTicket);
            if (result.ValidatedPrintTicket is not null)
            {
                KawaiiStudio.App.App.Log($"PRINT_TICKET_RESOLUTION={TargetPrintResolution}");
                return result.ValidatedPrintTicket;
            }
        }
        catch (Exception ex)
        {
            var reason = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            KawaiiStudio.App.App.Log($"PRINT_TICKET_RESOLUTION_FAILED reason={reason}");
        }

        return baseTicket;
    }

    private PrintTicket NormalizeTicketForOrientation(PrintQueue queue, PrintTicket baseTicket)
    {
        var orientation = baseTicket.PageOrientation ?? PageOrientation.Portrait;
        var mediaSize = baseTicket.PageMediaSize;
        if (mediaSize?.Width is null || mediaSize.Height is null)
        {
            return baseTicket;
        }

        var width = mediaSize.Width.Value;
        var height = mediaSize.Height.Value;
        var isPortrait = orientation is PageOrientation.Portrait or PageOrientation.ReversePortrait;
        var isLandscape = orientation is PageOrientation.Landscape or PageOrientation.ReverseLandscape;

        var needsSwap = (isPortrait && width > height) || (isLandscape && height > width);
        if (!needsSwap)
        {
            return baseTicket;
        }

        var overrideTicket = new PrintTicket
        {
            PageOrientation = orientation,
            PageMediaSize = new PageMediaSize(height, width)
        };

        try
        {
            var result = queue.MergeAndValidatePrintTicket(baseTicket, overrideTicket);
            if (result.ValidatedPrintTicket is not null)
            {
                KawaiiStudio.App.App.Log("PRINT_TICKET_ORIENTATION_NORMALIZED");
                return result.ValidatedPrintTicket;
            }
        }
        catch (Exception ex)
        {
            var reason = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            KawaiiStudio.App.App.Log($"PRINT_TICKET_ORIENTATION_FAILED reason={reason}");
        }

        return baseTicket;
    }

    private string? ResolveTicketPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        if (Path.IsPathRooted(rawPath))
        {
            return rawPath;
        }

        return Path.Combine(_configRoot, rawPath);
    }

    private static FixedDocument BuildDocument(
        BitmapSource image,
        int sheetCount,
        double pageWidth,
        double pageHeight,
        bool fitToPage)
    {
        var document = new FixedDocument
        {
            DocumentPaginator = { PageSize = new Size(pageWidth, pageHeight) }
        };

        for (var i = 0; i < sheetCount; i++)
        {
            var page = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight
            };

            var scaleInset = fitToPage ? TwoBySixScaleInset : 1.0;
            var imageWidth = pageWidth * scaleInset;
            var imageHeight = pageHeight * scaleInset;

            var imageElement = new Image
            {
                Source = image,
                Width = imageWidth,
                Height = imageHeight,
                Stretch = fitToPage ? Stretch.Uniform : Stretch.UniformToFill
            };
            RenderOptions.SetBitmapScalingMode(imageElement, BitmapScalingMode.HighQuality);

            FixedPage.SetLeft(imageElement, (pageWidth - imageWidth) / 2.0);
            FixedPage.SetTop(imageElement, (pageHeight - imageHeight) / 2.0);
            page.Children.Add(imageElement);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);
            document.Pages.Add(pageContent);
        }

        return document;
    }

    private static BitmapSource RotateBitmap(BitmapSource source, double rotationDegrees)
    {
        var transformed = new TransformedBitmap(source, new RotateTransform(rotationDegrees));
        transformed.Freeze();
        return transformed;
    }

    private static (double width, double height) NormalizePageSizeForOrientation(
        PageOrientation? orientation,
        double width,
        double height)
    {
        var isPortrait = orientation is PageOrientation.Portrait or PageOrientation.ReversePortrait;
        var isLandscape = orientation is PageOrientation.Landscape or PageOrientation.ReverseLandscape;

        if (isPortrait && width > height)
        {
            return (height, width);
        }

        if (isLandscape && height > width)
        {
            return (height, width);
        }

        return (width, height);
    }
}
