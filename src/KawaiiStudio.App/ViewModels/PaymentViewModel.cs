using System;
using System.Text;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class PaymentViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly RelayCommand _markPaidCommand;
    private string _summaryText = string.Empty;

    public PaymentViewModel(NavigationService navigation, SessionService session, ThemeCatalogService themeCatalog)
        : base(themeCatalog, "payment")
    {
        _navigation = navigation;
        _session = session;

        _markPaidCommand = new RelayCommand(MarkPaid, () => !_session.Current.IsPaid);
        MarkPaidCommand = _markPaidCommand;
        BackCommand = new RelayCommand(() => _navigation.Navigate("frame"));
        CancelCommand = new RelayCommand(Cancel, () => !_session.Current.IsPaid);
    }

    public ICommand MarkPaidCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand CancelCommand { get; }

    public string SummaryText
    {
        get => _summaryText;
        private set
        {
            _summaryText = value;
            OnPropertyChanged();
        }
    }

    public bool IsPaid => _session.Current.IsPaid;

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var session = _session.Current;
        var builder = new StringBuilder();

        builder.AppendLine($"Size: {FormatSize(session.Size)}");
        builder.AppendLine($"Quantity: {FormatValue(session.Quantity)}");
        builder.AppendLine($"Layout: {FormatLayout(session.Layout)}");
        builder.AppendLine($"Template: {FormatValue(session.TemplateType)}");
        builder.AppendLine($"Category: {FormatValue(session.Category?.Name)}");
        builder.AppendLine($"Frame: {FormatValue(session.Frame?.Name)}");

        SummaryText = builder.ToString().TrimEnd();
        OnPropertyChanged(nameof(IsPaid));
        _markPaidCommand.RaiseCanExecuteChanged();
    }

    private void MarkPaid()
    {
        _session.Current.MarkPaid();
        OnPropertyChanged(nameof(IsPaid));
        _markPaidCommand.RaiseCanExecuteChanged();
        _navigation.Navigate("capture");
    }

    private void Cancel()
    {
        if (_session.Current.IsPaid)
        {
            return;
        }

        _navigation.Navigate("home");
    }

    private static string FormatSize(PrintSize? size)
    {
        return size switch
        {
            PrintSize.TwoBySix => "2x6",
            PrintSize.FourBySix => "4x6",
            _ => "Not set"
        };
    }

    private static string FormatLayout(LayoutStyle? layout)
    {
        return layout switch
        {
            LayoutStyle.TwoSlots => "Style A (2 slots)",
            LayoutStyle.FourSlots => "Style B (4 slots)",
            LayoutStyle.SixSlots => "Style C (6 slots)",
            _ => "Not set"
        };
    }

    private static string FormatValue(object? value)
    {
        return value is null ? "Not set" : value.ToString() ?? "Not set";
    }
}
