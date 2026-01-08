using System.Collections.Generic;
using System.Linq;

namespace KawaiiStudio.App.Models;

public sealed class ThemeBackground
{
    public ThemeBackground(string screenKey, string folderPath, IReadOnlyList<string> assets)
    {
        ScreenKey = screenKey;
        FolderPath = folderPath;
        Assets = assets;
    }

    public string ScreenKey { get; }
    public string FolderPath { get; }
    public IReadOnlyList<string> Assets { get; }

    public int AssetCount => Assets.Count;
    public string? PreviewAssetPath => Assets.FirstOrDefault();
    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewAssetPath);

    public string AssetNames => Assets.Count == 0
        ? "No assets"
        : string.Join(", ", Assets.Select(asset => System.IO.Path.GetFileName(asset)));
}
