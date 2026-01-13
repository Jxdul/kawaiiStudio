using System;
using System.IO;
using System.Printing;

namespace KawaiiStudio.App.Services;

public sealed class PrintTicketExportService
{
    private readonly SettingsService _settings;
    private readonly AppPaths _paths;

    public PrintTicketExportService(SettingsService settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;
    }

    public (bool ok, string? path, string? error) ExportTwoBySixTicket()
    {
        var queue = ResolveQueue();
        if (queue is null)
        {
            return (false, null, "print_queue_missing");
        }

        try
        {
            queue.Refresh();
        }
        catch
        {
            // Ignore refresh failures; still attempt to export.
        }

        var ticket = queue.UserPrintTicket ?? queue.DefaultPrintTicket ?? new PrintTicket();
        var directory = Path.Combine(_paths.ConfigRoot, "printtickets");
        Directory.CreateDirectory(directory);
        var fullPath = Path.Combine(directory, "2x6-cut.xml");

        try
        {
            using var stream = File.Create(fullPath);
            ticket.SaveTo(stream);
        }
        catch (Exception ex)
        {
            var reason = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            return (false, null, $"print_ticket_save_failed:{reason}");
        }

        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
        {
            return (false, fullPath, "print_ticket_empty");
        }

        var relativePath = Path.GetRelativePath(_paths.ConfigRoot, fullPath);
        return (true, relativePath, null);
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
            // Fall back to default.
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
}
