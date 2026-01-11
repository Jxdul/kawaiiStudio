using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class FrameOverrideService
{
    public bool TryLoad(string framePath, out FrameOverrideDefinition? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(framePath))
        {
            return false;
        }

        var overridePath = GetOverridePath(framePath);
        if (!File.Exists(overridePath))
        {
            return false;
        }

        using var stream = File.OpenRead(overridePath);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var slots = ReadSlots(document.RootElement);
        if (slots.Count == 0)
        {
            return false;
        }

        TemplateSlot? qr = null;
        if (document.RootElement.TryGetProperty("qr", out var qrElement) && TryReadRect(qrElement, out var qrRect))
        {
            qr = qrRect;
        }

        definition = new FrameOverrideDefinition(slots, qr);
        return true;
    }

    public void Save(string framePath, FrameOverrideDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(framePath))
        {
            return;
        }

        var overridePath = GetOverridePath(framePath);
        var payload = new Dictionary<string, object?>
        {
            ["slots"] = BuildSlotDtos(definition.Slots),
            ["qr"] = definition.Qr is null ? null : BuildSlotDto(definition.Qr)
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        Directory.CreateDirectory(Path.GetDirectoryName(overridePath) ?? ".");
        var json = JsonSerializer.Serialize(payload, options);
        File.WriteAllText(overridePath, json);
    }

    public bool Delete(string framePath)
    {
        if (string.IsNullOrWhiteSpace(framePath))
        {
            return false;
        }

        var overridePath = GetOverridePath(framePath);
        if (!File.Exists(overridePath))
        {
            return false;
        }

        File.Delete(overridePath);
        return true;
    }

    public string GetOverridePath(string framePath)
    {
        var directory = Path.GetDirectoryName(framePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(framePath);
        return Path.Combine(directory, $"{name}.layout.json");
    }

    private static List<TemplateSlot> ReadSlots(JsonElement root)
    {
        var slots = new List<TemplateSlot>();
        if (!root.TryGetProperty("slots", out var slotsElement) || slotsElement.ValueKind != JsonValueKind.Array)
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

        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static List<Dictionary<string, int>> BuildSlotDtos(IEnumerable<TemplateSlot> slots)
    {
        var results = new List<Dictionary<string, int>>();
        foreach (var slot in slots)
        {
            results.Add(BuildSlotDto(slot));
        }

        return results;
    }

    private static Dictionary<string, int> BuildSlotDto(TemplateSlot slot)
    {
        return new Dictionary<string, int>
        {
            ["x"] = slot.X,
            ["y"] = slot.Y,
            ["w"] = slot.Width,
            ["h"] = slot.Height
        };
    }
}
