namespace KawaiiStudio.App.Models;

public sealed class LayoutOption
{
    public LayoutOption(LayoutStyle style, string displayName, int slots, string templateType)
    {
        Style = style;
        DisplayName = displayName;
        Slots = slots;
        TemplateType = templateType;
    }

    public LayoutStyle Style { get; }
    public string DisplayName { get; }
    public int Slots { get; }
    public string TemplateType { get; }
}
