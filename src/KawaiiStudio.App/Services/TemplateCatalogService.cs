using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class TemplateCatalogService
{
    private readonly string _templatePath;
    private Dictionary<string, TemplateDefinition>? _templates;

    public TemplateCatalogService(string templatePath)
    {
        _templatePath = templatePath;
    }

    public TemplateDefinition? GetTemplate(string? templateType)
    {
        if (string.IsNullOrWhiteSpace(templateType))
        {
            return null;
        }

        EnsureLoaded();
        return _templates!.TryGetValue(templateType, out var template) ? template : null;
    }

    public void Reload()
    {
        _templates = null;
    }

    private void EnsureLoaded()
    {
        if (_templates is not null)
        {
            return;
        }

        _templates = LoadTemplates();
    }

    private Dictionary<string, TemplateDefinition> LoadTemplates()
    {
        var templates = new Dictionary<string, TemplateDefinition>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_templatePath))
        {
            return templates;
        }

        using var stream = File.OpenRead(_templatePath);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return templates;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var key = property.Name;
            var entry = property.Value;
            if (!TryReadCanvas(entry, out var canvas))
            {
                continue;
            }

            var slots = ReadSlots(entry);
            if (slots.Count == 0)
            {
                continue;
            }

            TemplateSlot? qr = null;
            if (entry.TryGetProperty("qr", out var qrElement) && TryReadRect(qrElement, out var qrRect))
            {
                qr = qrRect;
            }

            templates[key] = new TemplateDefinition(key, canvas, slots, qr);
        }

        return templates;
    }

    private static bool TryReadCanvas(JsonElement entry, out TemplateCanvas canvas)
    {
        canvas = new TemplateCanvas(0, 0);
        if (!entry.TryGetProperty("canvas", out var canvasElement))
        {
            return false;
        }

        if (!TryGetInt(canvasElement, "width", out var width)
            || !TryGetInt(canvasElement, "height", out var height))
        {
            return false;
        }

        canvas = new TemplateCanvas(width, height);
        return width > 0 && height > 0;
    }

    private static List<TemplateSlot> ReadSlots(JsonElement entry)
    {
        var slots = new List<TemplateSlot>();
        if (!entry.TryGetProperty("slots", out var slotsElement) || slotsElement.ValueKind != JsonValueKind.Array)
        {
            return slots;
        }

        foreach (var slotElement in slotsElement.EnumerateArray())
        {
            if (TryReadRect(slotElement, out var slot))
            {
                slots.Add(slot);
            }
        }

        return slots;
    }

    private static bool TryReadRect(JsonElement element, out TemplateSlot slot)
    {
        slot = new TemplateSlot(0, 0, 0, 0);
        if (!TryGetInt(element, "x", out var x)
            || !TryGetInt(element, "y", out var y)
            || !TryGetInt(element, "w", out var width)
            || !TryGetInt(element, "h", out var height))
        {
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        slot = new TemplateSlot(x, y, width, height);
        return true;
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
