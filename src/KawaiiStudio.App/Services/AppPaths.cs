using System;
using System.IO;

namespace KawaiiStudio.App.Services;

public sealed class AppPaths
{
    private static readonly string[] ConfigFolderNames = ["Config", "config"];

    public AppPaths(string configRoot, string framesRoot, string themeRoot)
    {
        ConfigRoot = configRoot;
        FramesRoot = framesRoot;
        ThemeRoot = themeRoot;
    }

    public string ConfigRoot { get; }
    public string FramesRoot { get; }
    public string ThemeRoot { get; }

    public static AppPaths Resolve()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var configRoot = FindConfigRoot(baseDirectory) ?? Path.Combine(baseDirectory, "Config");
        var framesRoot = Path.Combine(configRoot, "frames");
        var themeRoot = Path.Combine(configRoot, "themes", "default");

        return new AppPaths(configRoot, framesRoot, themeRoot);
    }

    private static string? FindConfigRoot(string baseDirectory)
    {
        var current = new DirectoryInfo(baseDirectory);
        while (current != null)
        {
            foreach (var folderName in ConfigFolderNames)
            {
                var candidate = Path.Combine(current.FullName, folderName);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            current = current.Parent;
        }

        return null;
    }
}
