using System;

namespace KawaiiStudio.App.Services;

public sealed class CardPaymentEventArgs : EventArgs
{
    public CardPaymentEventArgs(decimal amount, string? message = null)
    {
        Amount = amount;
        Message = message;
    }

    public decimal Amount { get; }
    public string? Message { get; }
}
