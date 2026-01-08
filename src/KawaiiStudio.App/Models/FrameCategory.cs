using System.Collections.Generic;
using System.Linq;

namespace KawaiiStudio.App.Models;

public sealed class FrameCategory
{
    public FrameCategory(string name, string templateType, string folderPath, IReadOnlyList<FrameItem> frames)
    {
        Name = name;
        TemplateType = templateType;
        FolderPath = folderPath;
        Frames = frames;
    }

    public string Name { get; }
    public string TemplateType { get; }
    public string FolderPath { get; }
    public IReadOnlyList<FrameItem> Frames { get; }

    public int FrameCount => Frames.Count;
    public string? PreviewImagePath => Frames.FirstOrDefault()?.FilePath;
    public bool HasPreview => !string.IsNullOrWhiteSpace(PreviewImagePath);

    public string FrameNames => Frames.Count == 0
        ? "No frames"
        : string.Join(", ", Frames.Select(frame => frame.Name));
}
