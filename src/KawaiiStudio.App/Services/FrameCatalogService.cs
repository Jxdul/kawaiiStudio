using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class FrameCatalogService
{
    private readonly string _framesRoot;

    public FrameCatalogService(string framesRoot)
    {
        _framesRoot = framesRoot;
    }

    public IReadOnlyList<FrameCategory> Load()
    {
        var categories = new List<FrameCategory>();

        Load2x6Categories(categories);
        Load4x6Categories(categories);

        return categories;
    }

    private void Load2x6Categories(List<FrameCategory> categories)
    {
        var twoBySixRoot = Path.Combine(_framesRoot, "2x6");
        if (!Directory.Exists(twoBySixRoot))
        {
            return;
        }

        foreach (var categoryDir in Directory.EnumerateDirectories(twoBySixRoot))
        {
            var frames = LoadFrames(categoryDir);
            categories.Add(new FrameCategory(
                name: Path.GetFileName(categoryDir),
                templateType: "2x6_4slots",
                folderPath: categoryDir,
                frames: frames));
        }
    }

    private void Load4x6Categories(List<FrameCategory> categories)
    {
        var fourBySixRoot = Path.Combine(_framesRoot, "4x6");
        if (!Directory.Exists(fourBySixRoot))
        {
            return;
        }

        LoadSlotCategories(categories, fourBySixRoot, "2slots", "4x6_2slots");
        LoadSlotCategories(categories, fourBySixRoot, "4slots", "4x6_4slots");
        LoadSlotCategories(categories, fourBySixRoot, "6slots", "4x6_6slots");
    }

    private void LoadSlotCategories(List<FrameCategory> categories, string fourBySixRoot, string slotFolder, string templateType)
    {
        var slotRoot = Path.Combine(fourBySixRoot, slotFolder);
        if (!Directory.Exists(slotRoot))
        {
            return;
        }

        foreach (var categoryDir in Directory.EnumerateDirectories(slotRoot))
        {
            var frames = LoadFrames(categoryDir);
            categories.Add(new FrameCategory(
                name: Path.GetFileName(categoryDir),
                templateType: templateType,
                folderPath: categoryDir,
                frames: frames));
        }
    }

    private static IReadOnlyList<FrameItem> LoadFrames(string categoryDir)
    {
        var frames = Directory.EnumerateFiles(categoryDir)
            .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .Select(path => new FrameItem(Path.GetFileNameWithoutExtension(path), path))
            .OrderBy(frame => frame.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return frames;
    }
}
