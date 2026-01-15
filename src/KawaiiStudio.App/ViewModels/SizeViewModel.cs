using System;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class SizeViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly SettingsService _settings;
    private string _staffPin = string.Empty;
    private string _pendingStaffPin = string.Empty;
    private string _staffPinError = string.Empty;
    private bool _isStaffPinPromptVisible;

    public SizeViewModel(
        NavigationService navigation,
        SessionService session,
        ThemeCatalogService themeCatalog,
        SettingsService settings)
        : base(themeCatalog, "size")
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;

        SelectSizeCommand = new RelayCommand<PrintSize>(SelectSize);
        BackCommand = new RelayCommand(() => _navigation.Navigate("home"));
        StaffCommand = new RelayCommand(RequestStaffAccess);
        StaffPinInputCommand = new RelayCommand<string>(StaffPinInput);
        StaffPinBackspaceCommand = new RelayCommand(StaffPinBackspace);
        StaffPinClearCommand = new RelayCommand(StaffPinClear);
        StaffPinConfirmCommand = new RelayCommand(StaffPinConfirm);
        StaffPinCancelCommand = new RelayCommand(StaffPinCancel);
    }

    public ICommand SelectSizeCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand StaffCommand { get; }
    public ICommand StaffPinInputCommand { get; }
    public ICommand StaffPinBackspaceCommand { get; }
    public ICommand StaffPinClearCommand { get; }
    public ICommand StaffPinConfirmCommand { get; }
    public ICommand StaffPinCancelCommand { get; }

    public bool IsStaffPinPromptVisible
    {
        get => _isStaffPinPromptVisible;
        private set
        {
            _isStaffPinPromptVisible = value;
            OnPropertyChanged();
        }
    }

    public string StaffPinDisplay => new string('*', _pendingStaffPin.Length);

    public string StaffPinError
    {
        get => _staffPinError;
        private set
        {
            _staffPinError = value;
            OnPropertyChanged();
        }
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        ClearStaffPinPrompt();
    }

    private void SelectSize(PrintSize size)
    {
        _session.Current.SetSize(size);
        KawaiiStudio.App.App.Log($"SIZE_SELECTED value={FormatSize(size)}");
        _navigation.Navigate("quantity");
    }

    private static string FormatSize(PrintSize size)
    {
        return size == PrintSize.TwoBySix ? "2x6" : "4x6";
    }

    private void RequestStaffAccess()
    {
        _settings.Reload();
        var configuredPin = _settings.StaffPin;
        if (string.IsNullOrWhiteSpace(configuredPin))
        {
            ClearStaffPinPrompt();
            _navigation.Navigate("staff");
            return;
        }

        _staffPin = configuredPin.Trim();
        SetPendingStaffPin(string.Empty);
        StaffPinError = string.Empty;
        IsStaffPinPromptVisible = true;
    }

    private void StaffPinInput(string input)
    {
        if (!IsStaffPinPromptVisible || string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var digit = input.Trim();
        if (digit.Length != 1 || digit[0] < '0' || digit[0] > '9')
        {
            return;
        }

        SetPendingStaffPin(_pendingStaffPin + digit);
        StaffPinError = string.Empty;
    }

    private void StaffPinBackspace()
    {
        if (!IsStaffPinPromptVisible || _pendingStaffPin.Length == 0)
        {
            return;
        }

        SetPendingStaffPin(_pendingStaffPin[..^1]);
        StaffPinError = string.Empty;
    }

    private void StaffPinClear()
    {
        if (!IsStaffPinPromptVisible)
        {
            return;
        }

        SetPendingStaffPin(string.Empty);
        StaffPinError = string.Empty;
    }

    private void StaffPinConfirm()
    {
        if (!IsStaffPinPromptVisible)
        {
            return;
        }

        if (string.Equals(_pendingStaffPin, _staffPin, StringComparison.Ordinal))
        {
            ClearStaffPinPrompt();
            _navigation.Navigate("staff");
            return;
        }

        StaffPinError = "Incorrect PIN";
        SetPendingStaffPin(string.Empty);
    }

    private void StaffPinCancel()
    {
        if (!IsStaffPinPromptVisible)
        {
            return;
        }

        ClearStaffPinPrompt();
    }

    private void ClearStaffPinPrompt()
    {
        _staffPin = string.Empty;
        SetPendingStaffPin(string.Empty);
        StaffPinError = string.Empty;
        IsStaffPinPromptVisible = false;
    }

    private void SetPendingStaffPin(string value)
    {
        _pendingStaffPin = value ?? string.Empty;
        OnPropertyChanged(nameof(StaffPinDisplay));
    }
}
