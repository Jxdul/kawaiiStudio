using System.Threading;
using System.Threading.Tasks;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public interface IPrinterProvider
{
    Task<(bool ok, string? jobId, string? error)> PrintAsync(
        string imagePath,
        int sheetCount,
        PrintSize? size,
        CancellationToken cancellationToken);
}
