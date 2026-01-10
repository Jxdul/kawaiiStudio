namespace KawaiiStudio.App.ViewModels;

public sealed class StaffSettingEntry : ViewModelBase
{
    private string _value = string.Empty;

    public StaffSettingEntry(string key, string label, string value)
    {
        Key = key;
        Label = label;
        _value = value;
    }

    public string Key { get; }
    public string Label { get; }

    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            OnPropertyChanged();
        }
    }
}
