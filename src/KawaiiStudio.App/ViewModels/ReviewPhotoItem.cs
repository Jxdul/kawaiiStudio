namespace KawaiiStudio.App.ViewModels;

public sealed class ReviewPhotoItem : ViewModelBase
{
    private int _slotIndex;

    public ReviewPhotoItem(int index, string filePath)
    {
        Index = index;
        FilePath = filePath;
    }

    public int Index { get; }
    public string FilePath { get; }

    public int SlotIndex
    {
        get => _slotIndex;
        private set
        {
            _slotIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(SlotLabel));
        }
    }

    public bool IsSelected => SlotIndex > 0;

    public string SlotLabel => SlotIndex > 0 ? SlotIndex.ToString() : string.Empty;

    public void SetSlotIndex(int slotIndex)
    {
        SlotIndex = slotIndex;
    }
}
