using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace KawaiiStudio.App.Services;

public static class ImageCache
{
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static BitmapSource? GetOrLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out var cached))
            {
                return cached;
            }
        }

        var loaded = LoadBitmap(path);
        if (loaded is null)
        {
            return null;
        }

        lock (CacheLock)
        {
            Cache[path] = loaded;
        }

        return loaded;
    }

    public static int Preload(IEnumerable<string> paths)
    {
        var loadedCount = 0;
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (GetOrLoad(path) is not null)
            {
                loadedCount++;
            }
        }

        return loadedCount;
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
