using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class StaffViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SettingsService _settings;
    private readonly RelayCommand _confirmEntryCommand;
    private readonly RelayCommand _cancelEntryCommand;
    private string _maxQuantity = string.Empty;
    private string _tokenValue = string.Empty;
    private string _printName = string.Empty;
    private string _cashCom = string.Empty;
    private bool _testMode;
    private StaffSettingEntry? _selectedEntry;
    private string _pendingEntryValue = string.Empty;

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
        CloseAppCommand = new RelayCommand(CloseApp);
        SelectEntryCommand = new RelayCommand<StaffSettingEntry>(SelectEntry);
        _confirmEntryCommand = new RelayCommand(ConfirmEntry, () => SelectedEntry is not null);
        ConfirmEntryCommand = _confirmEntryCommand;
        _cancelEntryCommand = new RelayCommand(CancelEntry, () => SelectedEntry is not null);
        CancelEntryCommand = _cancelEntryCommand;
        NumpadInputCommand = new RelayCommand<string>(NumpadInput);
        NumpadBackspaceCommand = new RelayCommand(NumpadBackspace);
        NumpadClearCommand = new RelayCommand(NumpadClear);
        OpenTemplateEditorCommand = new RelayCommand(() => _navigation.Navigate("template_editor"));
        BackCommand = new RelayCommand(() => _navigation.Navigate("home"));
    }

    public ObservableCollection<StaffSettingEntry> PricingEntries { get; } = new();
    public ObservableCollection<StaffSettingEntry> TimeoutEntries { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand CloseAppCommand { get; }
    public ICommand SelectEntryCommand { get; }
    public ICommand ConfirmEntryCommand { get; }
    public ICommand CancelEntryCommand { get; }
    public ICommand NumpadInputCommand { get; }
    public ICommand NumpadBackspaceCommand { get; }
    public ICommand NumpadClearCommand { get; }
    public ICommand OpenTemplateEditorCommand { get; }
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

    public bool TestMode
    {
        get => _testMode;
        set
        {
            _testMode = value;
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
            _confirmEntryCommand.RaiseCanExecuteChanged();
            _cancelEntryCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsNumpadEnabled => SelectedEntry is not null;

    public string PendingEntryValue
    {
        get => _pendingEntryValue;
        private set
        {
            _pendingEntryValue = value;
            OnPropertyChanged();
        }
    }

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
        TimeoutEntries.Clear();

        foreach (var entry in BuildPriceEntries())
        {
            var value = _settings.GetValue(entry.Key, entry.DefaultValue);
            PricingEntries.Add(new StaffSettingEntry(entry.Key, entry.Label, value));
        }

        foreach (var entry in BuildTimeoutEntries())
        {
            var value = _settings.GetValue(entry.Key, entry.DefaultValue);
            TimeoutEntries.Add(new StaffSettingEntry(entry.Key, entry.Label, value));
        }

        MaxQuantity = _settings.GetValue("MAX_QUANTITY", "10");
        TokenValue = _settings.GetValue("TOKEN_VALUE", "1");
        PrintName = _settings.GetValue("PrintName", "DS-RX1");
        CashCom = _settings.GetValue("cash_COM", "COM4");
        TestMode = string.Equals(_settings.GetValue("TEST_MODE", "false"), "true", System.StringComparison.OrdinalIgnoreCase);
        SelectedEntry = null;
        PendingEntryValue = string.Empty;
    }

    private void Save()
    {
        foreach (var entry in PricingEntries)
        {
            _settings.SetValue(entry.Key, entry.Value);
        }

        foreach (var entry in TimeoutEntries)
        {
            _settings.SetValue(entry.Key, entry.Value);
        }

        _settings.SetValue("MAX_QUANTITY", MaxQuantity);
        _settings.SetValue("TOKEN_VALUE", TokenValue);
        _settings.SetValue("PrintName", PrintName);
        _settings.SetValue("cash_COM", CashCom);
        _settings.SetValue("TEST_MODE", TestMode ? "true" : "false");

        _settings.Save();
    }

    private void SelectEntry(StaffSettingEntry entry)
    {
        SelectedEntry = entry;
        PendingEntryValue = entry.Value ?? string.Empty;
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

        var current = PendingEntryValue ?? string.Empty;
        if (current == "0")
        {
            current = string.Empty;
        }

        PendingEntryValue = current + digit;
    }

    private void NumpadBackspace()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        var current = PendingEntryValue ?? string.Empty;
        if (current.Length == 0)
        {
            return;
        }

        PendingEntryValue = current[..^1];
    }

    private void NumpadClear()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        PendingEntryValue = string.Empty;
    }

    private void ConfirmEntry()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        SelectedEntry.Value = PendingEntryValue ?? string.Empty;
        SelectedEntry = null;
    }

    private void CancelEntry()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        PendingEntryValue = SelectedEntry.Value ?? string.Empty;
        SelectedEntry = null;
    }

    private void CloseApp()
    {
        KawaiiStudio.App.App.Log("APP_CLOSE_REQUESTED");
        Application.Current.Shutdown();
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
            ("PRICE1_46", "4x6 (2 prints, any layout)", "15"),
            ("PRICE2_46", "4x6 (4 prints, any layout)", "30"),
            ("PRICE3_46", "4x6 (6 prints, any layout)", "45"),
            ("PRICE4_46", "4x6 (8 prints, any layout)", "60"),
            ("PRICE5_46", "4x6 (10 prints, any layout)", "75")
        };
    }

    private static IEnumerable<(string Key, string Label, string DefaultValue)> BuildTimeoutEntries()
    {
        return new[]
        {
            ("TIMEOUT_STARTUP", "Startup timeout (sec)", "45"),
            ("TIMEOUT_HOME", "Home timeout (sec)", "45"),
            ("TIMEOUT_SIZE", "Size timeout (sec)", "45"),
            ("TIMEOUT_QUANTITY", "Quantity timeout (sec)", "45"),
            ("TIMEOUT_LAYOUT", "Layout timeout (sec)", "45"),
            ("TIMEOUT_CATEGORY", "Category timeout (sec)", "45"),
            ("TIMEOUT_FRAME", "Frame timeout (sec)", "45"),
            ("TIMEOUT_PAYMENT", "Payment timeout (sec)", "45"),
            ("TIMEOUT_CAPTURE", "Capture timeout (sec)", "45"),
            ("TIMEOUT_REVIEW", "Review timeout (sec)", "45"),
            ("TIMEOUT_FINALIZE", "Finalize timeout (sec)", "45"),
            ("TIMEOUT_PRINTING", "Printing timeout (sec)", "45"),
            ("TIMEOUT_THANK_YOU", "Thank you timeout (sec)", "45"),
            ("TIMEOUT_LIBRARY", "Library timeout (sec)", "45"),
            ("TIMEOUT_STAFF", "Staff timeout (sec)", "45")
        };
    }
}
