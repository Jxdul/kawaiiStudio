using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public sealed class SimulatedCashAcceptorProvider : ICashAcceptorProvider
{
    private readonly HashSet<int> _allowedBills;
    private decimal _remainingAmount;
    private bool _hasRemainingAmount;
    private bool _acceptingEnabled;

    public SimulatedCashAcceptorProvider(IEnumerable<int>? allowedBills = null)
    {
        _allowedBills = NormalizeAllowedBills(allowedBills);
    }

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

        if (!_acceptingEnabled)
        {
            BillRejected?.Invoke(this, new CashAcceptorEventArgs(amount, "intake_disabled"));
            return;
        }

        if (_hasRemainingAmount && (amount <= 0 || amount > _remainingAmount))
        {
            var reason = amount <= 0 ? "invalid_amount" : "overpayment";
            BillRejected?.Invoke(this, new CashAcceptorEventArgs(amount, reason));
            return;
        }

        if (_allowedBills.Contains(amount))
        {
            BillAccepted?.Invoke(this, new CashAcceptorEventArgs(amount));
            return;
        }

        BillRejected?.Invoke(this, new CashAcceptorEventArgs(amount, "unsupported_denomination"));
    }

    public void UpdateRemainingAmount(decimal amount)
    {
        _remainingAmount = amount;
        _hasRemainingAmount = true;
        _acceptingEnabled = _remainingAmount > 0m;
    }

    private static HashSet<int> NormalizeAllowedBills(IEnumerable<int>? allowedBills)
    {
        if (allowedBills is null)
        {
            return new HashSet<int> { 5, 10, 20 };
        }

        var results = new HashSet<int>();
        foreach (var bill in allowedBills)
        {
            if (bill > 0)
            {
                results.Add(bill);
            }
        }

        if (results.Count == 0)
        {
            results.Add(5);
            results.Add(10);
            results.Add(20);
        }

        return results;
    }
}
