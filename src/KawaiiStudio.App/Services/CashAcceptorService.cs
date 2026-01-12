using System;
using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public sealed class CashAcceptorService : ICashAcceptorProvider
{
    private ICashAcceptorProvider _provider;

    public CashAcceptorService(ICashAcceptorProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        HookProvider(_provider);
    }

    public ICashAcceptorProvider Provider => _provider;

    public bool IsConnected => _provider.IsConnected;

    public event EventHandler<CashAcceptorEventArgs>? BillAccepted;
    public event EventHandler<CashAcceptorEventArgs>? BillRejected;

    public Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        return _provider.ConnectAsync(cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        return _provider.DisconnectAsync(cancellationToken);
    }

    public void SimulateBillInserted(int amount)
    {
        _provider.SimulateBillInserted(amount);
    }

    public void UpdateRemainingAmount(decimal amount)
    {
        _provider.UpdateRemainingAmount(amount);
    }

    public void UseProvider(ICashAcceptorProvider provider)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        if (ReferenceEquals(_provider, provider))
        {
            return;
        }

        UnhookProvider(_provider);
        _provider = provider;
        HookProvider(_provider);
    }

    private void HookProvider(ICashAcceptorProvider provider)
    {
        provider.BillAccepted += HandleBillAccepted;
        provider.BillRejected += HandleBillRejected;
    }

    private void UnhookProvider(ICashAcceptorProvider provider)
    {
        provider.BillAccepted -= HandleBillAccepted;
        provider.BillRejected -= HandleBillRejected;
    }

    private void HandleBillAccepted(object? sender, CashAcceptorEventArgs e)
    {
        BillAccepted?.Invoke(this, e);
    }

    private void HandleBillRejected(object? sender, CashAcceptorEventArgs e)
    {
        BillRejected?.Invoke(this, e);
    }
}
