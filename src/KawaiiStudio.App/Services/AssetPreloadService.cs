using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public sealed class AssetPreloadService
{
    private readonly FrameCatalogService _frameCatalog;
    private readonly ThemeCatalogService _themeCatalog;

    public AssetPreloadService(FrameCatalogService frameCatalog, ThemeCatalogService themeCatalog)
    {
        _frameCatalog = frameCatalog;
        _themeCatalog = themeCatalog;
    }

    public Task PreloadAsync()
    {
        return Task.Run(PreloadInternal);
    }

    private void PreloadInternal()
    {
        try
        {
            var categories = _frameCatalog.Load();
            var framePaths = categories
                .SelectMany(category => category.Frames)
                .Select(frame => frame.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path));

            var backgrounds = _themeCatalog.LoadBackgrounds();
            var backgroundPaths = backgrounds
                .SelectMany(background => background.Assets)
                .Where(path => !string.IsNullOrWhiteSpace(path));

            var paths = framePaths
                .Concat(backgroundPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            KawaiiStudio.App.App.Log($"PRELOAD_ASSETS start total={paths.Count}");
            var loaded = ImageCache.Preload(paths);
            KawaiiStudio.App.App.Log($"PRELOAD_ASSETS_DONE loaded={loaded} total={paths.Count}");
        }
        catch (Exception ex)
        {
            var message = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            KawaiiStudio.App.App.Log($"PRELOAD_ASSETS_FAILED reason={message}");
        }
    }
}
