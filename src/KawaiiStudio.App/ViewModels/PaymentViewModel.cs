using System;
using System.Linq;
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
    private readonly ICardPaymentProvider _cardPayment;
    private readonly RelayCommand _insertFiveCommand;
    private readonly RelayCommand _insertTenCommand;
    private readonly RelayCommand _insertTwentyCommand;
    private readonly RelayCommand _selectCardCommand;
    private readonly RelayCommand _selectCashCommand;
    private readonly RelayCommand _startCardPaymentCommand;
    private readonly RelayCommand _approveCardPaymentCommand;
    private readonly RelayCommand _declineCardPaymentCommand;
    private readonly RelayCommand _simulateVisaCommand;
    private readonly RelayCommand _simulateDeclinedCommand;
    private readonly RelayCommand _simulateInsufficientFundsCommand;
    private readonly RelayCommand _simulateLostCardCommand;
    private readonly RelayCommand _simulateStolenCardCommand;
    private readonly RelayCommand _simulateExpiredCardCommand;
    private readonly RelayCommand _simulateProcessingErrorCommand;
    private readonly RelayCommand _backCommand;
    private string _summaryText = string.Empty;
    private string _cashStatusText = string.Empty;
    private string _totalPriceText = string.Empty;
    private string _cashDenominationsText = string.Empty;
    private string _cardStatusText = "Card reader ready.";
    private bool _isCashActive = true;
    private bool _isCardPaymentInProgress;
    private bool _isCardTestMode;

    public PaymentViewModel(
        NavigationService navigation,
        SessionService session,
        ThemeCatalogService themeCatalog,
        SettingsService settings,
        ICashAcceptorProvider cashAcceptor,
        ICardPaymentProvider cardPayment)
        : base(themeCatalog, "payment")
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;
        _cashAcceptor = cashAcceptor;
        _cardPayment = cardPayment;

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
        _startCardPaymentCommand = new RelayCommand(StartCardPayment, CanStartCardPayment);
        _approveCardPaymentCommand = new RelayCommand(ApproveCardPayment, CanResolveCardPayment);
        _declineCardPaymentCommand = new RelayCommand(DeclineCardPayment, CanResolveCardPayment);
        _simulateVisaCommand = new RelayCommand(() => SimulateTerminalTest("4242424242424242", "Visa"), CanSimulateTerminalTest);
        _simulateDeclinedCommand = new RelayCommand(() => SimulateTerminalTest("4000000000000002", "Decline"), CanSimulateTerminalTest);
        _simulateInsufficientFundsCommand = new RelayCommand(() => SimulateTerminalTest("4000000000009995", "Insufficient Funds"), CanSimulateTerminalTest);
        _simulateLostCardCommand = new RelayCommand(() => SimulateTerminalTest("4000000000009987", "Lost Card"), CanSimulateTerminalTest);
        _simulateStolenCardCommand = new RelayCommand(() => SimulateTerminalTest("4000000000009979", "Stolen Card"), CanSimulateTerminalTest);
        _simulateExpiredCardCommand = new RelayCommand(() => SimulateTerminalTest("4000000000000069", "Expired Card"), CanSimulateTerminalTest);
        _simulateProcessingErrorCommand = new RelayCommand(() => SimulateTerminalTest("4000000000000119", "Processing Error"), CanSimulateTerminalTest);
        StartCardPaymentCommand = _startCardPaymentCommand;
        ApproveCardPaymentCommand = _approveCardPaymentCommand;
        DeclineCardPaymentCommand = _declineCardPaymentCommand;
        SimulateVisaCommand = _simulateVisaCommand;
        SimulateDeclinedCommand = _simulateDeclinedCommand;
        SimulateInsufficientFundsCommand = _simulateInsufficientFundsCommand;
        SimulateLostCardCommand = _simulateLostCardCommand;
        SimulateStolenCardCommand = _simulateStolenCardCommand;
        SimulateExpiredCardCommand = _simulateExpiredCardCommand;
        SimulateProcessingErrorCommand = _simulateProcessingErrorCommand;
        _backCommand = new RelayCommand(() => _navigation.Navigate("frame"), () => !_session.Current.IsPaid);
        BackCommand = _backCommand;
        CancelCommand = new RelayCommand(Cancel, () => !_session.Current.IsPaid);

        _cashAcceptor.BillAccepted += HandleBillAccepted;
        _cashAcceptor.BillRejected += HandleBillRejected;
        _cardPayment.PaymentApproved += HandleCardApproved;
        _cardPayment.PaymentDeclined += HandleCardDeclined;
    }

    public ICommand InsertFiveCommand { get; }
    public ICommand InsertTenCommand { get; }
    public ICommand InsertTwentyCommand { get; }
    public ICommand SelectCardCommand { get; }
    public ICommand SelectCashCommand { get; }
    public ICommand StartCardPaymentCommand { get; }
    public ICommand ApproveCardPaymentCommand { get; }
    public ICommand DeclineCardPaymentCommand { get; }
    public ICommand SimulateVisaCommand { get; }
    public ICommand SimulateDeclinedCommand { get; }
    public ICommand SimulateInsufficientFundsCommand { get; }
    public ICommand SimulateLostCardCommand { get; }
    public ICommand SimulateStolenCardCommand { get; }
    public ICommand SimulateExpiredCardCommand { get; }
    public ICommand SimulateProcessingErrorCommand { get; }
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

    public string CardStatusText
    {
        get => _cardStatusText;
        private set
        {
            _cardStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsCardPaymentInProgress
    {
        get => _isCardPaymentInProgress;
        private set
        {
            if (_isCardPaymentInProgress == value)
            {
                return;
            }

            _isCardPaymentInProgress = value;
            OnPropertyChanged();
            UpdateInsertCommandState();
        }
    }

    public bool IsCardTestMode
    {
        get => _isCardTestMode;
        private set
        {
            if (_isCardTestMode == value)
            {
                return;
            }

            _isCardTestMode = value;
            OnPropertyChanged();
            UpdateInsertCommandState();
        }
    }

    public string CashStatusText
    {
        get => _cashStatusText;
        private set
        {
            _cashStatusText = value;
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

    public string CashDenominationsText
    {
        get => _cashDenominationsText;
        private set
        {
            _cashDenominationsText = value;
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
        UpdateCashStatus();
        UpdateCashDenominations();
        UpdateCardTestMode();
        OnPropertyChanged(nameof(IsPaid));
        UpdateInsertCommandState();
        _backCommand.RaiseCanExecuteChanged();
    }

    private void UpdateCashStatus()
    {
        var total = CalculateTotalPrice();
        var cashInserted = _session.Current.CashInserted;
        CashStatusText = total <= 0m
            ? "Cash: pricing not set"
            : $"Inserted: {FormatCurrency(cashInserted)} / {FormatCurrency(total)}";

        TotalPriceText = $"Total due: {FormatCurrency(total)}";
        _cashAcceptor.UpdateRemainingAmount(GetRemainingDue());
    }

    private void UpdateCashDenominations()
    {
        var denoms = _settings.CashDenominations
            .OrderBy(value => value)
            .ToList();

        if (denoms.Count == 0)
        {
            CashDenominationsText = "Accepts: see staff settings";
            return;
        }

        var formatted = string.Join(" / ", denoms.Select(value => $"${value}"));
        CashDenominationsText = $"Accepts: {formatted}";
    }

    private decimal CalculateTotalPrice()
    {
        var session = _session.Current;
        var total = _settings.GetPrice(session.TemplateType, session.Quantity);
        session.SetPriceTotal(total);
        return total;
    }

    private bool CanInsertBill()
    {
        return !_session.Current.IsPaid && GetRemainingDue() > 0m && IsCashActive && _settings.TestMode;
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
        StartCardPayment();
    }

    private void SelectCash()
    {
        if (_session.Current.IsPaid)
        {
            return;
        }

        SetPaymentMode(true);
    }

    private bool CanStartCardPayment()
    {
        return !_session.Current.IsPaid && IsCardActive && !IsCardPaymentInProgress && GetRemainingDue() > 0m;
    }

    private bool CanResolveCardPayment()
    {
        return !_session.Current.IsPaid && IsCardActive && IsCardPaymentInProgress;
    }

    private void StartCardPayment()
    {
        if (_session.Current.IsPaid || !IsCardActive)
        {
            return;
        }

        var amount = GetRemainingDue();
        if (amount <= 0m)
        {
            CardStatusText = "No balance due.";
            return;
        }

        CardStatusText = "Waiting for card...";
        IsCardPaymentInProgress = true;
        _ = StartCardPaymentAsync(amount);
        KawaiiStudio.App.App.Log($"CARD_PAYMENT_STARTED amount={amount:0.00}");
    }

    private void ApproveCardPayment()
    {
        _cardPayment.SimulateApprove();
    }

    private void DeclineCardPayment()
    {
        _cardPayment.SimulateDecline("declined");
    }

    private void UpdateCardTestMode()
    {
        IsCardTestMode = _settings.TestMode && _cardPayment is IStripeTerminalTestProvider;
    }

    private bool CanSimulateTerminalTest()
    {
        return !_session.Current.IsPaid && IsCardActive && IsCardPaymentInProgress && IsCardTestMode;
    }

    private async void SimulateTerminalTest(string cardNumber, string label)
    {
        if (!CanSimulateTerminalTest() || _cardPayment is not IStripeTerminalTestProvider testProvider)
        {
            return;
        }

        CardStatusText = $"Simulating {label}...";
        var ok = await testProvider.SimulatePaymentAsync(cardNumber, CancellationToken.None);
        if (!ok && !_session.Current.IsPaid)
        {
            CardStatusText = $"Simulation failed: {label}";
            IsCardPaymentInProgress = false;
        }
    }

    private void HandleCardApproved(object? sender, CardPaymentEventArgs e)
    {
        IsCardPaymentInProgress = false;
        CardStatusText = "Card approved.";
        KawaiiStudio.App.App.Log($"CARD_PAYMENT_APPROVED amount={e.Amount:0.00}");
        MarkPaid();
    }

    private void HandleCardDeclined(object? sender, CardPaymentEventArgs e)
    {
        IsCardPaymentInProgress = false;
        var reason = string.IsNullOrWhiteSpace(e.Message) ? "declined" : e.Message;
        CardStatusText = "Card declined. Try again or use cash.";
        KawaiiStudio.App.App.Log($"CARD_PAYMENT_DECLINED amount={e.Amount:0.00} reason={reason}");
    }

    private async System.Threading.Tasks.Task StartCardPaymentAsync(decimal amount)
    {
        if (!_cardPayment.IsConnected)
        {
            var connected = await _cardPayment.ConnectAsync(CancellationToken.None);
            if (!connected)
            {
                CardStatusText = "Card reader unavailable.";
                IsCardPaymentInProgress = false;
                KawaiiStudio.App.App.Log($"CARD_PAYMENT_FAILED amount={amount:0.00} reason=connect_failed");
                return;
            }
        }

        var started = await _cardPayment.StartPaymentAsync(amount, CancellationToken.None);
        if (!started && IsCardActive)
        {
            CardStatusText = "Card reader unavailable.";
            IsCardPaymentInProgress = false;
            KawaiiStudio.App.App.Log($"CARD_PAYMENT_FAILED amount={amount:0.00}");
        }
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
        _session.Current.AddCash(e.Amount);
        UpdateCashStatus();
        KawaiiStudio.App.App.Log($"PAYMENT_BILL_ACCEPTED amount={e.Amount:0.00} total={_session.Current.CashInserted:0.00}");

        var total = _session.Current.PriceTotal;
        if (total <= 0m)
        {
            total = CalculateTotalPrice();
        }

        if (total > 0m && _session.Current.CashInserted >= total)
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
        _cashAcceptor.UpdateRemainingAmount(0m);
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
        _ = _cardPayment.CancelAsync(CancellationToken.None);
        IsCardPaymentInProgress = false;
        _navigation.Navigate("home");
    }

    private void UpdateInsertCommandState()
    {
        _insertFiveCommand.RaiseCanExecuteChanged();
        _insertTenCommand.RaiseCanExecuteChanged();
        _insertTwentyCommand.RaiseCanExecuteChanged();
        _selectCardCommand.RaiseCanExecuteChanged();
        _selectCashCommand.RaiseCanExecuteChanged();
        _startCardPaymentCommand.RaiseCanExecuteChanged();
        _approveCardPaymentCommand.RaiseCanExecuteChanged();
        _declineCardPaymentCommand.RaiseCanExecuteChanged();
        _simulateVisaCommand.RaiseCanExecuteChanged();
        _simulateDeclinedCommand.RaiseCanExecuteChanged();
        _simulateInsufficientFundsCommand.RaiseCanExecuteChanged();
        _simulateLostCardCommand.RaiseCanExecuteChanged();
        _simulateStolenCardCommand.RaiseCanExecuteChanged();
        _simulateExpiredCardCommand.RaiseCanExecuteChanged();
        _simulateProcessingErrorCommand.RaiseCanExecuteChanged();
    }

    private decimal GetRemainingDue()
    {
        var total = _session.Current.PriceTotal;
        if (total <= 0m)
        {
            total = CalculateTotalPrice();
        }

        var remaining = total - _session.Current.CashInserted;
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
            _cashAcceptor.UpdateRemainingAmount(GetRemainingDue());
            _ = _cardPayment.CancelAsync(CancellationToken.None);
            _ = _cardPayment.DisconnectAsync(CancellationToken.None);
            IsCardPaymentInProgress = false;
            CardStatusText = "Card reader ready.";
            if (modeChanged)
            {
                KawaiiStudio.App.App.Log("PAYMENT_MODE cash");
            }
        }
        else
        {
            _ = _cashAcceptor.DisconnectAsync(CancellationToken.None);
            _ = _cardPayment.ConnectAsync(CancellationToken.None);
            CardStatusText = "Card reader ready.";
            if (modeChanged)
            {
                KawaiiStudio.App.App.Log("PAYMENT_MODE card");
            }
        }

        UpdateCardTestMode();
    }
}
