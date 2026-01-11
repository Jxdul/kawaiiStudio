using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class TemplateStorageService
{
    private readonly string _templatePath;

    public TemplateStorageService(string templatePath)
    {
        _templatePath = templatePath;
    }

    public IReadOnlyDictionary<string, TemplateDefinition> LoadAll()
    {
        var templates = new Dictionary<string, TemplateDefinition>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_templatePath))
        {
            return templates;
        }

        var json = File.ReadAllText(_templatePath);
        var options = BuildOptions();
        var raw = JsonSerializer.Deserialize<Dictionary<string, TemplateDefinitionDto>>(json, options);
        if (raw is null)
        {
            return templates;
        }

        foreach (var pair in raw)
        {
            var template = MapFromDto(pair.Key, pair.Value);
            if (template is not null)
            {
                templates[pair.Key] = template;
            }
        }

        return templates;
    }

    public void SaveTemplate(TemplateDefinition template)
    {
        var options = BuildOptions();
        var raw = LoadRawTemplates(options);
        raw[template.Key] = MapToDto(template);

        var ordered = raw
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        var json = JsonSerializer.Serialize(ordered, options);
        Directory.CreateDirectory(Path.GetDirectoryName(_templatePath) ?? ".");
        File.WriteAllText(_templatePath, json);
    }

    private Dictionary<string, TemplateDefinitionDto> LoadRawTemplates(JsonSerializerOptions options)
    {
        if (!File.Exists(_templatePath))
        {
            return new Dictionary<string, TemplateDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(_templatePath);
        var raw = JsonSerializer.Deserialize<Dictionary<string, TemplateDefinitionDto>>(json, options);
        return raw ?? new Dictionary<string, TemplateDefinitionDto>(StringComparer.OrdinalIgnoreCase);
    }

    private static TemplateDefinition? MapFromDto(string key, TemplateDefinitionDto dto)
    {
        if (dto.Canvas is null || dto.Slots is null)
        {
            return null;
        }

        var canvas = new TemplateCanvas(dto.Canvas.Width, dto.Canvas.Height);
        var slots = dto.Slots
            .Select(slot => new TemplateSlot(slot.X, slot.Y, slot.Width, slot.Height))
            .ToList();

        TemplateSlot? qr = null;
        if (dto.Qr is not null)
        {
            qr = new TemplateSlot(dto.Qr.X, dto.Qr.Y, dto.Qr.Width, dto.Qr.Height);
        }

        return new TemplateDefinition(key, canvas, slots, qr);
    }

    private static TemplateDefinitionDto MapToDto(TemplateDefinition template)
    {
        return new TemplateDefinitionDto
        {
            Canvas = new TemplateCanvasDto
            {
                Width = template.Canvas.Width,
                Height = template.Canvas.Height
            },
            Slots = template.Slots.Select(slot => new TemplateSlotDto
            {
                X = slot.X,
                Y = slot.Y,
                Width = slot.Width,
                Height = slot.Height
            }).ToList(),
            Qr = template.Qr is null
                ? null
                : new TemplateSlotDto
                {
                    X = template.Qr.X,
                    Y = template.Qr.Y,
                    Width = template.Qr.Width,
                    Height = template.Qr.Height
                }
        };
    }

    private static JsonSerializerOptions BuildOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true
        };
    }

    private sealed class TemplateDefinitionDto
    {
        [JsonPropertyName("canvas")]
        public TemplateCanvasDto? Canvas { get; set; }

        [JsonPropertyName("slots")]
        public List<TemplateSlotDto>? Slots { get; set; }

        [JsonPropertyName("qr")]
        public TemplateSlotDto? Qr { get; set; }
    }

    private sealed class TemplateCanvasDto
    {
        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }
    }

    private sealed class TemplateSlotDto
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("w")]
        public int Width { get; set; }

        [JsonPropertyName("h")]
        public int Height { get; set; }
    }
}
