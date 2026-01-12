using System;

namespace KawaiiStudio.App.Services;

public sealed class CashAcceptorEventArgs : EventArgs
{
    public CashAcceptorEventArgs(int amount, string? reason = null)
    {
        Amount = amount;
        Reason = reason;
    }

    public int Amount { get; }
    public string? Reason { get; }
}
