using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class StaffViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SettingsService _settings;
    private string _maxQuantity = string.Empty;
    private string _tokenValue = string.Empty;
    private string _printName = string.Empty;
    private string _cashCom = string.Empty;
    private StaffSettingEntry? _selectedEntry;

    public StaffViewModel(
        NavigationService navigation,
        ThemeCatalogService themeCatalog,
        SettingsService settings)
        : base(themeCatalog, "staff")
    {
        _navigation = navigation;
        _settings = settings;

        SaveCommand = new RelayCommand(Save);
        ReloadCommand = new RelayCommand(LoadFromSettings);
        SelectEntryCommand = new RelayCommand<StaffSettingEntry>(SelectEntry);
        NumpadInputCommand = new RelayCommand<string>(NumpadInput);
        NumpadBackspaceCommand = new RelayCommand(NumpadBackspace);
        NumpadClearCommand = new RelayCommand(NumpadClear);
        BackCommand = new RelayCommand(() => _navigation.Navigate("home"));
    }

    public ObservableCollection<StaffSettingEntry> PricingEntries { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand SelectEntryCommand { get; }
    public ICommand NumpadInputCommand { get; }
    public ICommand NumpadBackspaceCommand { get; }
    public ICommand NumpadClearCommand { get; }
    public ICommand BackCommand { get; }

    public string MaxQuantity
    {
        get => _maxQuantity;
        set
        {
            _maxQuantity = value;
            OnPropertyChanged();
        }
    }

    public string TokenValue
    {
        get => _tokenValue;
        set
        {
            _tokenValue = value;
            OnPropertyChanged();
        }
    }

    public string PrintName
    {
        get => _printName;
        set
        {
            _printName = value;
            OnPropertyChanged();
        }
    }

    public string CashCom
    {
        get => _cashCom;
        set
        {
            _cashCom = value;
            OnPropertyChanged();
        }
    }

    public StaffSettingEntry? SelectedEntry
    {
        get => _selectedEntry;
        private set
        {
            _selectedEntry = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNumpadEnabled));
        }
    }

    public bool IsNumpadEnabled => SelectedEntry is not null;

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        LoadFromSettings();
        KawaiiStudio.App.App.Log("STAFF_ACCESS");
    }

    private void LoadFromSettings()
    {
        _settings.Reload();
        PricingEntries.Clear();

        foreach (var entry in BuildPriceEntries())
        {
            var value = _settings.GetValue(entry.Key, entry.DefaultValue);
            PricingEntries.Add(new StaffSettingEntry(entry.Key, entry.Label, value));
        }

        MaxQuantity = _settings.GetValue("MAX_QUANTITY", "10");
        TokenValue = _settings.GetValue("TOKEN_VALUE", "1");
        PrintName = _settings.GetValue("PrintName", "DS-RX1");
        CashCom = _settings.GetValue("cash_COM", "COM4");
        SelectedEntry = null;
    }

    private void Save()
    {
        foreach (var entry in PricingEntries)
        {
            _settings.SetValue(entry.Key, entry.Value);
        }

        _settings.SetValue("MAX_QUANTITY", MaxQuantity);
        _settings.SetValue("TOKEN_VALUE", TokenValue);
        _settings.SetValue("PrintName", PrintName);
        _settings.SetValue("cash_COM", CashCom);

        _settings.Save();
    }

    private void SelectEntry(StaffSettingEntry entry)
    {
        SelectedEntry = entry;
    }

    private void NumpadInput(string input)
    {
        if (SelectedEntry is null || string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var digit = input.Trim();
        if (digit.Length != 1 || digit[0] < '0' || digit[0] > '9')
        {
            return;
        }

        var current = SelectedEntry.Value ?? string.Empty;
        if (current == "0")
        {
            current = string.Empty;
        }

        SelectedEntry.Value = current + digit;
    }

    private void NumpadBackspace()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var current = SelectedEntry.Value ?? string.Empty;
        if (current.Length == 0)
        {
            return;
        }

        SelectedEntry.Value = current[..^1];
    }

    private void NumpadClear()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        SelectedEntry.Value = string.Empty;
    }

    private static IEnumerable<(string Key, string Label, string DefaultValue)> BuildPriceEntries()
    {
        return new[]
        {
            ("PRICE1_26", "2x6 (2 prints)", "10"),
            ("PRICE2_26", "2x6 (4 prints)", "20"),
            ("PRICE3_26", "2x6 (6 prints)", "30"),
            ("PRICE4_26", "2x6 (8 prints)", "40"),
            ("PRICE5_26", "2x6 (10 prints)", "50"),
            ("PRICE1_46", "4x6 (2 prints)", "15"),
            ("PRICE2_46", "4x6 (4 prints)", "30"),
            ("PRICE3_46", "4x6 (6 prints)", "45"),
            ("PRICE4_46", "4x6 (8 prints)", "60"),
            ("PRICE5_46", "4x6 (10 prints)", "75")
        };
    }
}
