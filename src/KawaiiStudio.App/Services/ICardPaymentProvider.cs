using System;
using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public interface ICardPaymentProvider
{
    bool IsConnected { get; }

    event EventHandler<CardPaymentEventArgs>? PaymentApproved;
    event EventHandler<CardPaymentEventArgs>? PaymentDeclined;

    Task<bool> ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task<bool> StartPaymentAsync(decimal amount, CancellationToken cancellationToken);
    Task CancelAsync(CancellationToken cancellationToken);

    void SimulateApprove();
    void SimulateDecline(string? message = null);
}
