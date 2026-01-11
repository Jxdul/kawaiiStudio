using System;
using System.Collections.Generic;

namespace KawaiiStudio.App.Models;

public sealed class SessionState
{
    private readonly List<string> _capturedPhotos = new();
    private readonly Dictionary<int, int> _selectedMapping = new();

    public string SessionId { get; private set; } = string.Empty;
    public DateTime StartTime { get; private set; }
    public DateTime? EndTime { get; private set; }
    public string? SessionFolder { get; private set; }
    public string? PhotosFolder { get; private set; }
    public string? PreviewFramesFolder { get; private set; }
    public string? VideosFolder { get; private set; }

    public PrintSize? Size { get; private set; }
    public int? Quantity { get; private set; }
    public LayoutStyle? Layout { get; private set; }
    public FrameCategory? Category { get; private set; }
    public FrameItem? Frame { get; private set; }
    public bool IsPaid { get; private set; }
    public int TokensInserted { get; private set; }
    public decimal PriceTotal { get; private set; }
    public string PaymentStatus { get; private set; } = "Pending";
    public string? PaymentDetail { get; private set; }

    public IReadOnlyList<string> CapturedPhotos => _capturedPhotos;
    public IReadOnlyDictionary<int, int> SelectedMapping => _selectedMapping;

    public string? FinalImagePath { get; private set; }
    public string? VideoPath { get; private set; }
    public string? QrUrl { get; private set; }
    public string? PrintJobId { get; private set; }
    public string? PrintStatus { get; private set; }

    public string? TemplateType
    {
        get
        {
            return Size switch
            {
                PrintSize.TwoBySix => "2x6_4slots",
                PrintSize.FourBySix => Layout switch
                {
                    LayoutStyle.TwoSlots => "4x6_2slots",
                    LayoutStyle.FourSlots => "4x6_4slots",
                    LayoutStyle.SixSlots => "4x6_6slots",
                    _ => null
                },
                _ => null
            };
        }
    }

    public int? SlotCount
    {
        get
        {
            return Size switch
            {
                PrintSize.TwoBySix => 4,
                PrintSize.FourBySix => Layout switch
                {
                    LayoutStyle.TwoSlots => 2,
                    LayoutStyle.FourSlots => 4,
                    LayoutStyle.SixSlots => 6,
                    _ => null
                },
                _ => null
            };
        }
    }

    public void Reset(
        string sessionId,
        DateTime startTime,
        string sessionFolder,
        string photosFolder,
        string previewFramesFolder,
        string videosFolder)
    {
        SessionId = sessionId;
        StartTime = startTime;
        EndTime = null;
        SessionFolder = sessionFolder;
        PhotosFolder = photosFolder;
        PreviewFramesFolder = previewFramesFolder;
        VideosFolder = videosFolder;
        Size = null;
        Quantity = null;
        Layout = null;
        Category = null;
        Frame = null;
        ResetPayment();
        _capturedPhotos.Clear();
        _selectedMapping.Clear();
        FinalImagePath = null;
        VideoPath = null;
        QrUrl = null;
        PrintJobId = null;
        PrintStatus = null;
    }

    public void SetSize(PrintSize size)
    {
        Size = size;
        Layout = null;
        Category = null;
        Frame = null;
        ResetPayment();
    }

    public void SetQuantity(int quantity)
    {
        if (quantity <= 0 || quantity % 2 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be a positive even number.");
        }

        Quantity = quantity;
    }

    public void SetLayout(LayoutStyle layout)
    {
        Layout = layout;
        Category = null;
        Frame = null;
        ResetPayment();
    }

    public void SetCategory(FrameCategory category)
    {
        Category = category;
        Frame = null;
        ResetPayment();
    }

    public void SetFrame(FrameItem frame)
    {
        Frame = frame;
        ResetPayment();
    }

    public void MarkPaid()
    {
        IsPaid = true;
        PaymentStatus = "Paid";
        PaymentDetail = null;
    }

    public void AddTokens(int tokens)
    {
        if (tokens <= 0)
        {
            return;
        }

        TokensInserted += tokens;
    }

    public void SetPriceTotal(decimal total)
    {
        PriceTotal = total;
    }

    public void SetPaymentStatus(string status, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        PaymentStatus = status.Trim();
        PaymentDetail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        IsPaid = string.Equals(PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase);
    }

    public void AddCapturedPhoto(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _capturedPhotos.Add(filePath);
    }

    public void ClearCapturedPhotos()
    {
        _capturedPhotos.Clear();
    }

    public void SetSelectedMapping(int slotIndex, int photoIndex)
    {
        if (slotIndex <= 0 || photoIndex < 0)
        {
            return;
        }

        _selectedMapping[slotIndex] = photoIndex;
    }

    public void RemoveSelectedMapping(int slotIndex)
    {
        if (slotIndex <= 0)
        {
            return;
        }

        _selectedMapping.Remove(slotIndex);
    }

    public void ClearSelectedMapping()
    {
        _selectedMapping.Clear();
    }

    public void SetFinalImagePath(string? path)
    {
        FinalImagePath = string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public void SetVideoPath(string? path)
    {
        VideoPath = string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public void SetQrUrl(string? url)
    {
        QrUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
    }

    public void SetPrintJob(string? jobId, string? status = null)
    {
        PrintJobId = string.IsNullOrWhiteSpace(jobId) ? null : jobId.Trim();
        PrintStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
    }

    public void MarkCompleted(DateTime endTime)
    {
        EndTime = endTime;
    }

    private void ResetPayment()
    {
        IsPaid = false;
        TokensInserted = 0;
        PriceTotal = 0m;
        PaymentStatus = "Pending";
        PaymentDetail = null;
    }
}
