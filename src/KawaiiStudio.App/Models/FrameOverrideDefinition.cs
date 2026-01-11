using System.Collections.Generic;

namespace KawaiiStudio.App.Models;

public sealed class FrameOverrideDefinition
{
    public FrameOverrideDefinition(IReadOnlyList<TemplateSlot> slots, TemplateSlot? qr)
    {
        Slots = slots;
        Qr = qr;
    }

    public IReadOnlyList<TemplateSlot> Slots { get; }
    public TemplateSlot? Qr { get; }
}
