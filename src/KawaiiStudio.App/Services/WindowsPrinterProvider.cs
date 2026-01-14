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
    private const double DefaultPageWidth = 6 * 96;  // 6 inches @ 96 DPI
    private const double DefaultPageHeight = 4 * 96; // 4 inches @ 96 DPI
    private readonly SettingsService _settings;

    public WindowsPrinterProvider(SettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public Task<(bool ok, string? jobId, string? error)> PrintAsync(
        string imagePath,
        int copyCount,
        PrintSize size,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return Task.FromResult((false, (string?)null, "print_image_missing"));
        }

        if (copyCount <= 0)
        {
            return Task.FromResult((false, (string?)null, "print_copy_count_invalid"));
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

            return PrintInternal(imagePath, copyCount, size);
        }).Task;
    }

    private (bool ok, string? jobId, string? error) PrintInternal(string imagePath, int copyCount, PrintSize size)
    {
        var image = ImageCache.GetOrLoad(imagePath);
        if (image is null)
        {
            return (false, null, "print_image_load_failed");
        }

        var queue = ResolveQueue(size);
        if (queue is null)
        {
            return (false, null, "print_queue_missing");
        }

        var ticket = queue.DefaultPrintTicket ?? new PrintTicket();
        ticket.PageOrientation = PageOrientation.Landscape;
        ticket.PageMediaSize ??= new PageMediaSize(PageMediaSizeName.NorthAmerica4x6);

        var pageWidth = ticket.PageMediaSize.Width ?? DefaultPageWidth;
        var pageHeight = ticket.PageMediaSize.Height ?? DefaultPageHeight;

        var document = BuildDocument(image, copyCount, pageWidth, pageHeight);
        var writer = PrintQueue.CreateXpsDocumentWriter(queue);
        writer.Write(document.DocumentPaginator, ticket);

        var jobId = $"print_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{size}";
        KawaiiStudio.App.App.Log($"PRINT_SENT queue={queue.Name} copies={copyCount} size={size} job={jobId}");
        return (true, jobId, null);
    }

    private PrintQueue? ResolveQueue(PrintSize size)
    {
        var printerName = size == PrintSize.TwoBySix
            ? _settings.PrinterName2x6
            : _settings.PrinterName4x6;

        if (string.IsNullOrWhiteSpace(printerName))
        {
            KawaiiStudio.App.App.Log($"PRINT_QUEUE_MISSING size={size} name=empty");
            return null;
        }

        try
        {
            var queue = new PrintQueue(new PrintServer(), printerName);
            queue.Refresh();
            return queue;
        }
        catch (Exception ex)
        {
            var reason = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            KawaiiStudio.App.App.Log($"PRINT_QUEUE_FAILED size={size} name={printerName} reason={reason}");
            return null;
        }
    }

    private static FixedDocument BuildDocument(
        BitmapSource image,
        int copyCount,
        double pageWidth,
        double pageHeight)
    {
        var document = new FixedDocument
        {
            DocumentPaginator = { PageSize = new Size(pageWidth, pageHeight) }
        };

        for (var i = 0; i < copyCount; i++)
        {
            var page = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight
            };

            var imageElement = new Image
            {
                Source = image,
                Width = pageWidth,
                Height = pageHeight,
                Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapScalingMode(imageElement, BitmapScalingMode.HighQuality);

            FixedPage.SetLeft(imageElement, 0);
            FixedPage.SetTop(imageElement, 0);
            page.Children.Add(imageElement);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);
            document.Pages.Add(pageContent);
        }

        return document;
    }
}
