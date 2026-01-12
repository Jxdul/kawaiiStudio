using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public sealed class SimulatedCashAcceptorProvider : ICashAcceptorProvider
{
    private static readonly HashSet<int> AllowedBills = new() { 5, 10, 20 };

    public bool IsConnected { get; private set; }

    public event EventHandler<CashAcceptorEventArgs>? BillAccepted;
    public event EventHandler<CashAcceptorEventArgs>? BillRejected;

    public Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public void SimulateBillInserted(int amount)
    {
        if (!IsConnected)
        {
            BillRejected?.Invoke(this, new CashAcceptorEventArgs(amount, "not_connected"));
            return;
        }

        if (AllowedBills.Contains(amount))
        {
            BillAccepted?.Invoke(this, new CashAcceptorEventArgs(amount));
            return;
        }

        BillRejected?.Invoke(this, new CashAcceptorEventArgs(amount, "unsupported_denomination"));
    }
}
