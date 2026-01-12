using System;
using System.Text;
using System.Threading;
using System.Windows.Input;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class PaymentViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly SettingsService _settings;
    private readonly ICashAcceptorProvider _cashAcceptor;
    private readonly RelayCommand _insertFiveCommand;
    private readonly RelayCommand _insertTenCommand;
    private readonly RelayCommand _insertTwentyCommand;
    private readonly RelayCommand _selectCardCommand;
    private readonly RelayCommand _selectCashCommand;
    private readonly RelayCommand _backCommand;
    private string _summaryText = string.Empty;
    private string _tokenStatusText = string.Empty;
    private string _totalPriceText = string.Empty;
    private int _tokensRequired;
    private bool _isCashActive = true;

    public PaymentViewModel(
        NavigationService navigation,
        SessionService session,
        ThemeCatalogService themeCatalog,
        SettingsService settings,
        ICashAcceptorProvider cashAcceptor)
        : base(themeCatalog, "payment")
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;
        _cashAcceptor = cashAcceptor;

        _insertFiveCommand = new RelayCommand(() => InsertBill(5), CanInsertBill);
        _insertTenCommand = new RelayCommand(() => InsertBill(10), CanInsertBill);
        _insertTwentyCommand = new RelayCommand(() => InsertBill(20), CanInsertBill);
        InsertFiveCommand = _insertFiveCommand;
        InsertTenCommand = _insertTenCommand;
        InsertTwentyCommand = _insertTwentyCommand;
        _selectCardCommand = new RelayCommand(SelectCard, CanSelectCard);
        _selectCashCommand = new RelayCommand(SelectCash, CanSelectCash);
        SelectCardCommand = _selectCardCommand;
        SelectCashCommand = _selectCashCommand;
        _backCommand = new RelayCommand(() => _navigation.Navigate("frame"), () => !_session.Current.IsPaid);
        BackCommand = _backCommand;
        CancelCommand = new RelayCommand(Cancel, () => !_session.Current.IsPaid);

        _cashAcceptor.BillAccepted += HandleBillAccepted;
        _cashAcceptor.BillRejected += HandleBillRejected;
    }

    public ICommand InsertFiveCommand { get; }
    public ICommand InsertTenCommand { get; }
    public ICommand InsertTwentyCommand { get; }
    public ICommand SelectCardCommand { get; }
    public ICommand SelectCashCommand { get; }
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

    public bool IsCashActive
    {
        get => _isCashActive;
        private set
        {
            if (_isCashActive == value)
            {
                return;
            }

            _isCashActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCardActive));
            OnPropertyChanged(nameof(PromptText));
        }
    }

    public bool IsCardActive => !IsCashActive;

    public string PromptText =>
        IsCashActive
            ? "Insert cash now or select card payment."
            : "Card payment selected. Follow the card reader instructions.";

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
        SetPaymentMode(true);
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
        UpdateInsertCommandState();
        _backCommand.RaiseCanExecuteChanged();
    }

    private void UpdateTokenStatus()
    {
        var total = CalculateTotalPrice();
        var tokensInserted = _session.Current.TokensInserted;
        TokenStatusText = _tokensRequired <= 0
            ? "Cash: pricing not set"
            : $"Inserted: {FormatCurrency(tokensInserted)} / {FormatCurrency(total)}";

        TotalPriceText = $"Total due: {FormatCurrency(total)}";
    }

    private decimal CalculateTotalPrice()
    {
        var session = _session.Current;
        var total = _settings.GetPrice(session.TemplateType, session.Quantity);
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

    private bool CanInsertBill()
    {
        return !_session.Current.IsPaid && _tokensRequired > 0 && IsCashActive;
    }

    private bool CanSelectCard()
    {
        return !_session.Current.IsPaid && IsCashActive;
    }

    private bool CanSelectCash()
    {
        return !_session.Current.IsPaid && IsCardActive;
    }

    private void SelectCard()
    {
        if (_session.Current.IsPaid)
        {
            return;
        }

        SetPaymentMode(false);
    }

    private void SelectCash()
    {
        if (_session.Current.IsPaid)
        {
            return;
        }

        SetPaymentMode(true);
    }

    private void InsertBill(int amount)
    {
        if (_session.Current.IsPaid)
        {
            return;
        }

        var remaining = GetRemainingDue();
        if (remaining <= 0m)
        {
            KawaiiStudio.App.App.Log($"PAYMENT_BILL_REJECTED amount={amount} reason=already_paid");
            return;
        }

        if (amount > remaining)
        {
            KawaiiStudio.App.App.Log($"PAYMENT_BILL_REJECTED amount={amount} reason=overpayment remaining={remaining:0.00}");
            return;
        }

        _cashAcceptor.SimulateBillInserted(amount);
    }

    private void HandleBillAccepted(object? sender, CashAcceptorEventArgs e)
    {
        _session.Current.AddTokens(e.Amount);
        UpdateTokenStatus();
        KawaiiStudio.App.App.Log($"PAYMENT_BILL_ACCEPTED amount={e.Amount} total={_session.Current.TokensInserted}");

        if (_tokensRequired > 0 && _session.Current.TokensInserted >= _tokensRequired)
        {
            MarkPaid();
        }
    }

    private void HandleBillRejected(object? sender, CashAcceptorEventArgs e)
    {
        var reason = string.IsNullOrWhiteSpace(e.Reason) ? "unknown" : e.Reason;
        KawaiiStudio.App.App.Log($"PAYMENT_BILL_REJECTED amount={e.Amount} reason={reason}");
    }

    private void MarkPaid()
    {
        _session.Current.MarkPaid();
        OnPropertyChanged(nameof(IsPaid));
        UpdateInsertCommandState();
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

    private void UpdateInsertCommandState()
    {
        _insertFiveCommand.RaiseCanExecuteChanged();
        _insertTenCommand.RaiseCanExecuteChanged();
        _insertTwentyCommand.RaiseCanExecuteChanged();
        _selectCardCommand.RaiseCanExecuteChanged();
        _selectCashCommand.RaiseCanExecuteChanged();
    }

    private decimal GetRemainingDue()
    {
        var total = _session.Current.PriceTotal;
        if (total <= 0m)
        {
            total = CalculateTotalPrice();
        }

        var remaining = total - _session.Current.TokensInserted;
        return remaining < 0m ? 0m : remaining;
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

    private void SetPaymentMode(bool useCash)
    {
        var modeChanged = useCash != IsCashActive;
        if (modeChanged)
        {
            IsCashActive = useCash;
        }

        UpdateInsertCommandState();

        if (useCash)
        {
            _ = _cashAcceptor.ConnectAsync(CancellationToken.None);
            if (modeChanged)
            {
                KawaiiStudio.App.App.Log("PAYMENT_MODE cash");
            }
        }
        else
        {
            _ = _cashAcceptor.DisconnectAsync(CancellationToken.None);
            if (modeChanged)
            {
                KawaiiStudio.App.App.Log("PAYMENT_MODE card");
            }
        }
    }
}
