using System;
using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public sealed class SimulatedCardPaymentProvider : ICardPaymentProvider
{
    private decimal _pendingAmount;
    private bool _inProgress;

    public bool IsConnected { get; private set; }

    public event EventHandler<CardPaymentEventArgs>? PaymentApproved;
    public event EventHandler<CardPaymentEventArgs>? PaymentDeclined;

    public Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = false;
        _inProgress = false;
        _pendingAmount = 0m;
        return Task.CompletedTask;
    }

    public Task<bool> StartPaymentAsync(decimal amount, CancellationToken cancellationToken)
    {
        if (!IsConnected || amount <= 0m)
        {
            return Task.FromResult(false);
        }

        _pendingAmount = amount;
        _inProgress = true;
        return Task.FromResult(true);
    }

    public Task CancelAsync(CancellationToken cancellationToken)
    {
        _inProgress = false;
        _pendingAmount = 0m;
        return Task.CompletedTask;
    }

    public void SimulateApprove()
    {
        if (!_inProgress)
        {
            return;
        }

        var amount = _pendingAmount;
        _pendingAmount = 0m;
        _inProgress = false;
        PaymentApproved?.Invoke(this, new CardPaymentEventArgs(amount));
    }

    public void SimulateDecline(string? message = null)
    {
        if (!_inProgress)
        {
            return;
        }

        var amount = _pendingAmount;
        _pendingAmount = 0m;
        _inProgress = false;
        PaymentDeclined?.Invoke(this, new CardPaymentEventArgs(amount, message));
    }
}
