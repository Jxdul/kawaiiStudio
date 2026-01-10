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
    private readonly SettingsService _settings;
    private readonly RelayCommand _addTokenCommand;
    private readonly RelayCommand _backCommand;
    private string _summaryText = string.Empty;
    private string _tokenStatusText = string.Empty;
    private string _totalPriceText = string.Empty;
    private int _tokensRequired;

    public PaymentViewModel(
        NavigationService navigation,
        SessionService session,
        ThemeCatalogService themeCatalog,
        SettingsService settings)
        : base(themeCatalog, "payment")
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;

        _addTokenCommand = new RelayCommand(AddToken, CanAddToken);
        AddTokenCommand = _addTokenCommand;
        _backCommand = new RelayCommand(() => _navigation.Navigate("frame"), () => !_session.Current.IsPaid);
        BackCommand = _backCommand;
        CancelCommand = new RelayCommand(Cancel, () => !_session.Current.IsPaid);
    }

    public ICommand AddTokenCommand { get; }
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

    public string TokenStatusText
    {
        get => _tokenStatusText;
        private set
        {
            _tokenStatusText = value;
            OnPropertyChanged();
        }
    }

    public string TotalPriceText
    {
        get => _totalPriceText;
        private set
        {
            _totalPriceText = value;
            OnPropertyChanged();
        }
    }

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
        builder.AppendLine($"Total: {FormatCurrency(CalculateTotalPrice())}");

        SummaryText = builder.ToString().TrimEnd();
        UpdateTokenStatus();
        OnPropertyChanged(nameof(IsPaid));
        _addTokenCommand.RaiseCanExecuteChanged();
        _backCommand.RaiseCanExecuteChanged();
    }

    private void UpdateTokenStatus()
    {
        var total = CalculateTotalPrice();
        var tokensInserted = _session.Current.TokensInserted;
        TokenStatusText = _tokensRequired <= 0
            ? "Tokens: pricing not set"
            : $"Tokens: {tokensInserted} / {_tokensRequired}";

        TotalPriceText = $"Total due: {FormatCurrency(total)}";
    }

    private decimal CalculateTotalPrice()
    {
        var session = _session.Current;
        var total = _settings.GetPrice(session.Size, session.Quantity);
        session.SetPriceTotal(total);
        _tokensRequired = CalculateTokensRequired(total, _settings.TokenValue);
        return total;
    }

    private static int CalculateTokensRequired(decimal totalPrice, decimal valuePerToken)
    {
        if (totalPrice <= 0m || valuePerToken <= 0m)
        {
            return 0;
        }

        return (int)Math.Ceiling(totalPrice / valuePerToken);
    }

    private bool CanAddToken()
    {
        return !_session.Current.IsPaid && _tokensRequired > 0;
    }

    private void AddToken()
    {
        if (_session.Current.IsPaid)
        {
            return;
        }

        _session.Current.AddTokens(5);
        UpdateTokenStatus();
        KawaiiStudio.App.App.Log($"PAYMENT_TOKEN_ADDED tokens={_session.Current.TokensInserted} required={_tokensRequired}");

        if (_tokensRequired > 0 && _session.Current.TokensInserted >= _tokensRequired)
        {
            MarkPaid();
        }
    }

    private void MarkPaid()
    {
        _session.Current.MarkPaid();
        OnPropertyChanged(nameof(IsPaid));
        _addTokenCommand.RaiseCanExecuteChanged();
        _backCommand.RaiseCanExecuteChanged();
        KawaiiStudio.App.App.Log($"PAYMENT_COMPLETED total={_session.Current.PriceTotal:0.00}");
        _navigation.Navigate("capture");
    }

    private void Cancel()
    {
        if (_session.Current.IsPaid)
        {
            return;
        }

        KawaiiStudio.App.App.Log("PAYMENT_CANCELED");
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

    private static string FormatCurrency(decimal amount)
    {
        return $"${amount:0.00}";
    }
}
