using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class ThemeCatalogService
{
    private readonly string _themeRoot;
    private readonly object _cacheLock = new();
    private IReadOnlyList<ThemeBackground>? _cachedBackgrounds;
    private Dictionary<string, string?>? _backgroundMap;

    public ThemeCatalogService(string themeRoot)
    {
        _themeRoot = themeRoot;
    }

    public IReadOnlyList<ThemeBackground> LoadBackgrounds()
    {
        lock (_cacheLock)
        {
            if (_cachedBackgrounds is not null)
            {
                return _cachedBackgrounds;
            }
        }

        var backgroundsRoot = Path.Combine(_themeRoot, "backgrounds");
        if (!Directory.Exists(backgroundsRoot))
        {
            lock (_cacheLock)
            {
                _cachedBackgrounds = Array.Empty<ThemeBackground>();
                _backgroundMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                return _cachedBackgrounds;
            }
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

        lock (_cacheLock)
        {
            _cachedBackgrounds = results;
            _backgroundMap = BuildBackgroundMap(results);
            return _cachedBackgrounds;
        }
    }

    public string? GetBackgroundPath(string screenKey)
    {
        if (string.IsNullOrWhiteSpace(screenKey))
        {
            return null;
        }

        var normalized = NormalizeKey(screenKey);
        var map = GetBackgroundMap();
        return map.TryGetValue(normalized, out var asset) ? asset : null;
    }

    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, string?> GetBackgroundMap()
    {
        lock (_cacheLock)
        {
            if (_backgroundMap is not null)
            {
                return _backgroundMap;
            }
        }

        var backgrounds = LoadBackgrounds();
        lock (_cacheLock)
        {
            _backgroundMap ??= BuildBackgroundMap(backgrounds);
            return _backgroundMap;
        }
    }

    private static Dictionary<string, string?> BuildBackgroundMap(IEnumerable<ThemeBackground> backgrounds)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var background in backgrounds)
        {
            var key = NormalizeKey(background.ScreenKey);
            map[key] = background.Assets.FirstOrDefault();
        }

        return map;
    }

    private static string NormalizeKey(string key)
    {
        return key.Replace('\\', '/').Trim('/');
    }
}
