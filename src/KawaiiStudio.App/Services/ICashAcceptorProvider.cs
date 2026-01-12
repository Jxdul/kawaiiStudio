using System;
using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public interface ICashAcceptorProvider
{
    bool IsConnected { get; }

    event EventHandler<CashAcceptorEventArgs>? BillAccepted;
    event EventHandler<CashAcceptorEventArgs>? BillRejected;

    Task<bool> ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);

    void SimulateBillInserted(int amount);
}
