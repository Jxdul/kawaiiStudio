using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class ThemeCatalogService
{
    private readonly string _themeRoot;

    public ThemeCatalogService(string themeRoot)
    {
        _themeRoot = themeRoot;
    }

    public IReadOnlyList<ThemeBackground> LoadBackgrounds()
    {
        var backgroundsRoot = Path.Combine(_themeRoot, "backgrounds");
        if (!Directory.Exists(backgroundsRoot))
        {
            return Array.Empty<ThemeBackground>();
        }

        var directories = Directory.EnumerateDirectories(backgroundsRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<ThemeBackground>();
        foreach (var directory in directories)
        {
            var relativeKey = Path.GetRelativePath(backgroundsRoot, directory);
            if (string.IsNullOrWhiteSpace(relativeKey) || relativeKey == ".")
            {
                continue;
            }

            var assets = Directory.EnumerateFiles(directory)
                .Where(IsImageFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            results.Add(new ThemeBackground(relativeKey.Replace(Path.DirectorySeparatorChar, '/'), directory, assets));
        }

        return results;
    }

    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }
}
