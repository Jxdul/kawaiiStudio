namespace KawaiiStudio.App.Models;

public sealed class FrameItem
{
    public FrameItem(string name, string filePath)
    {
        Name = name;
        FilePath = filePath;
    }

    public string Name { get; }
    public string FilePath { get; }
}
