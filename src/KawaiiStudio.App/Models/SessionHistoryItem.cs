using System;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Models;

public sealed class SessionHistoryItem
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime SessionDate { get; set; }
    public string SessionFolder { get; set; } = string.Empty;
    public string? FinalImagePath { get; set; }
    public PrintSize? Size { get; set; }
    public int? Quantity { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal? PaymentAmount { get; set; }
    public string? QrUrl { get; set; }
    public bool HasFinalImage { get; set; }
    public bool HasQrUrl { get; set; }

    public string DisplayDate => SessionDate.ToString("yyyy-MM-dd HH:mm:ss");
    public string DisplaySize => Size switch
    {
        PrintSize.TwoBySix => "2x6",
        PrintSize.FourBySix => "4x6",
        _ => "Unknown"
    };
    public string DisplayPayment => PaymentMethod switch
    {
        "cash" => $"Cash ${PaymentAmount:F2}",
        "card" => $"Card ${PaymentAmount:F2}",
        _ => "N/A"
    };
}
