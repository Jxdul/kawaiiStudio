using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public interface IStripeTerminalTestProvider
{
    Task<bool> SimulatePaymentAsync(string cardNumber, CancellationToken cancellationToken);
}
