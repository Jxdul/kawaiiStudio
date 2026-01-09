using System;

namespace KawaiiStudio.App.Models;

public sealed class SessionState
{
    public PrintSize? Size { get; private set; }
    public int? Quantity { get; private set; }
    public LayoutStyle? Layout { get; private set; }
    public FrameCategory? Category { get; private set; }
    public FrameItem? Frame { get; private set; }
    public bool IsPaid { get; private set; }

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

    public void Reset()
    {
        Size = null;
        Quantity = null;
        Layout = null;
        Category = null;
        Frame = null;
        IsPaid = false;
    }

    public void SetSize(PrintSize size)
    {
        Size = size;
        Layout = null;
        Category = null;
        Frame = null;
        IsPaid = false;
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
        IsPaid = false;
    }

    public void SetCategory(FrameCategory category)
    {
        Category = category;
        Frame = null;
        IsPaid = false;
    }

    public void SetFrame(FrameItem frame)
    {
        Frame = frame;
        IsPaid = false;
    }

    public void MarkPaid()
    {
        IsPaid = true;
    }
}
