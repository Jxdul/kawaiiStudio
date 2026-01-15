using System;

namespace KawaiiStudio.App.Services;

public sealed class CardPaymentEventArgs : EventArgs
{
    public CardPaymentEventArgs(decimal amount, string? message = null, string? paymentIntentId = null)
    {
        Amount = amount;
        Message = message;
        PaymentIntentId = paymentIntentId;
    }

    public decimal Amount { get; }
    public string? Message { get; }
    public string? PaymentIntentId { get; }
}
