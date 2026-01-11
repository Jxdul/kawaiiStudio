using System.Collections.Generic;

namespace KawaiiStudio.App.Models;

public sealed class TemplateDefinition
{
    public TemplateDefinition(string key, TemplateCanvas canvas, IReadOnlyList<TemplateSlot> slots, TemplateSlot? qr)
    {
        Key = key;
        Canvas = canvas;
        Slots = slots;
        Qr = qr;
    }

    public string Key { get; }
    public TemplateCanvas Canvas { get; }
    public IReadOnlyList<TemplateSlot> Slots { get; }
    public TemplateSlot? Qr { get; }
}

public sealed class TemplateCanvas
{
    public TemplateCanvas(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
}

public sealed class TemplateSlot
{
    public TemplateSlot(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
}
