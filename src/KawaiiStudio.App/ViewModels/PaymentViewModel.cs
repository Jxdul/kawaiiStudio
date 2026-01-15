using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using KawaiiStudio.App.Models;
using KawaiiStudio.App.Services;

namespace KawaiiStudio.App.ViewModels;

public sealed class PaymentViewModel : ScreenViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly SessionService _session;
    private readonly SettingsService _settings;
    private readonly FinanceTrackingService _financeTracking;
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
    private readonly RelayCommand _simulateVisaDebitCommand;
    private readonly RelayCommand _simulateMastercardCommand;
    private readonly RelayCommand _simulateAmexCommand;
    private readonly RelayCommand _simulateDiscoverCommand;
    private readonly RelayCommand _simulateJcbCommand;
    private readonly RelayCommand _simulateUnionPayCommand;
    private readonly RelayCommand _simulateInteracCommand;
    private readonly RelayCommand<CardTestOption> _simulateCardOptionCommand;
    private readonly RelayCommand _simulateDeclinedCommand;
    private readonly RelayCommand _simulateInsufficientFundsCommand;
    private readonly RelayCommand _simulateLostCardCommand;
    private readonly RelayCommand _simulateStolenCardCommand;
    private readonly RelayCommand _simulateExpiredCardCommand;
    private readonly RelayCommand _simulateProcessingErrorCommand;
    private readonly RelayCommand _backCommand;
    private readonly Dispatcher? _dispatcher;
    private string _summaryText = string.Empty;
    private string _cashStatusText = string.Empty;
    private string _totalPriceText = string.Empty;
    private string _cashDenominationsText = string.Empty;
    private string _cardStatusText = "Card reader ready.";
    private string _cashErrorText = string.Empty;
    private string? _lastCashRejectReason;
    private bool _isCashActive = true;
    private bool _isCardPaymentInProgress;
    private bool _isCardTestMode;
    private bool _cashTransactionLogged;
    private bool _cardTransactionLogged;

    public PaymentViewModel(
        NavigationService navigation,
        SessionService session,
        ThemeCatalogService themeCatalog,
        SettingsService settings,
        FinanceTrackingService financeTracking,
        ICashAcceptorProvider cashAcceptor,
        ICardPaymentProvider cardPayment)
        : base(themeCatalog, "payment")
    {
        _navigation = navigation;
        _session = session;
        _settings = settings;
        _financeTracking = financeTracking;
        _cashAcceptor = cashAcceptor;
        _cardPayment = cardPayment;
        _dispatcher = System.Windows.Application.Current?.Dispatcher;

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
        _simulateVisaDebitCommand = new RelayCommand(() => SimulateTerminalTest("4000056655665556", "Visa Debit"), CanSimulateTerminalTest);
        _simulateMastercardCommand = new RelayCommand(() => SimulateTerminalTest("5555555555554444", "Mastercard"), CanSimulateTerminalTest);
        _simulateAmexCommand = new RelayCommand(() => SimulateTerminalTest("378282246310005", "Amex"), CanSimulateTerminalTest);
        _simulateDiscoverCommand = new RelayCommand(() => SimulateTerminalTest("6011111111111117", "Discover"), CanSimulateTerminalTest);
        _simulateJcbCommand = new RelayCommand(() => SimulateTerminalTest("3566002020360505", "JCB"), CanSimulateTerminalTest);
        _simulateUnionPayCommand = new RelayCommand(() => SimulateTerminalTest("6200000000000005", "UnionPay"), CanSimulateTerminalTest);
        _simulateInteracCommand = new RelayCommand(() => SimulateTerminalTest("4506445006931933", "Interac"), CanSimulateTerminalTest);
        _simulateCardOptionCommand = new RelayCommand<CardTestOption>(SimulateCardOption, CanSimulateCardOption);
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
        SimulateVisaDebitCommand = _simulateVisaDebitCommand;
        SimulateMastercardCommand = _simulateMastercardCommand;
        SimulateAmexCommand = _simulateAmexCommand;
        SimulateDiscoverCommand = _simulateDiscoverCommand;
        SimulateJcbCommand = _simulateJcbCommand;
        SimulateUnionPayCommand = _simulateUnionPayCommand;
        SimulateInteracCommand = _simulateInteracCommand;
        SimulateCardOptionCommand = _simulateCardOptionCommand;
        SimulateDeclinedCommand = _simulateDeclinedCommand;
        SimulateInsufficientFundsCommand = _simulateInsufficientFundsCommand;
        SimulateLostCardCommand = _simulateLostCardCommand;
        SimulateStolenCardCommand = _simulateStolenCardCommand;
        SimulateExpiredCardCommand = _simulateExpiredCardCommand;
        SimulateProcessingErrorCommand = _simulateProcessingErrorCommand;
        _backCommand = new RelayCommand(() => _navigation.Navigate("frame"), () => !_session.Current.IsPaid);
        BackCommand = _backCommand;
        CancelCommand = new RelayCommand(Cancel, () => !_session.Current.IsPaid);

        CardTestOptions = BuildCardTestOptions();
        StandardCardTests = CardTestOptions.Where(option => option.Category == "Standard").ToList();
        SuccessCardTests = CardTestOptions.Where(option => option.Category == "Success").ToList();
        ErrorCardTests = CardTestOptions.Where(option => option.Category == "Error").ToList();

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
    public ICommand SimulateVisaDebitCommand { get; }
    public ICommand SimulateMastercardCommand { get; }
    public ICommand SimulateAmexCommand { get; }
    public ICommand SimulateDiscoverCommand { get; }
    public ICommand SimulateJcbCommand { get; }
    public ICommand SimulateUnionPayCommand { get; }
    public ICommand SimulateInteracCommand { get; }
    public ICommand SimulateCardOptionCommand { get; }
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

    public string CashErrorText
    {
        get => _cashErrorText;
        private set
        {
            if (_cashErrorText == value)
            {
                return;
            }

            _cashErrorText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCashError));
        }
    }

    public bool HasCashError => !string.IsNullOrWhiteSpace(_cashErrorText);

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        ResetTransactionTracking();
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
        _lastCashRejectReason = null;
        CashErrorText = string.Empty;
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
        if (total <= 0m)
        {
            CashStatusText = "Cash: pricing not set";
        }
        else
        {
            var status = $"Inserted: {FormatCurrency(cashInserted)} / {FormatCurrency(total)}";
            if (!string.IsNullOrWhiteSpace(_lastCashRejectReason))
            {
                status += $" (last: {FormatCashReason(_lastCashRejectReason)})";
            }

            CashStatusText = status;
        }

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
        IsCardTestMode = _cardPayment is IStripeTerminalTestProvider;
    }

    private bool CanSimulateTerminalTest()
    {
        return !_session.Current.IsPaid && IsCardActive && IsCardPaymentInProgress && IsCardTestMode;
    }

    private bool CanSimulateCardOption(CardTestOption option)
    {
        return option is not null && CanSimulateTerminalTest();
    }

    private void SimulateCardOption(CardTestOption option)
    {
        if (option is null)
        {
            return;
        }

        SimulateTerminalTest(option.CardNumber, option.Label);
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
        RunOnUiThread(() =>
        {
            IsCardPaymentInProgress = false;
            CardStatusText = "Card approved.";
            KawaiiStudio.App.App.Log($"CARD_PAYMENT_APPROVED amount={e.Amount:0.00}");
            RecordCashPayment(GetCashAmountToRecord());
            RecordCardPayment(e.Amount, e.PaymentIntentId);
            MarkPaid();
        });
    }

    private void HandleCardDeclined(object? sender, CardPaymentEventArgs e)
    {
        RunOnUiThread(() =>
        {
            IsCardPaymentInProgress = false;
            var reason = string.IsNullOrWhiteSpace(e.Message) ? "declined" : e.Message;
            CardStatusText = "Card declined. Try again or use cash.";
            KawaiiStudio.App.App.Log($"CARD_PAYMENT_DECLINED amount={e.Amount:0.00} reason={reason}");
        });
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
            _lastCashRejectReason = "already_paid";
            CashErrorText = MapCashError(_lastCashRejectReason);
            UpdateCashStatus();
            return;
        }

        var remaining = GetRemainingDue();
        if (remaining <= 0m)
        {
            KawaiiStudio.App.App.Log($"PAYMENT_BILL_REJECTED amount={amount} reason=already_paid");
            _lastCashRejectReason = "already_paid";
            CashErrorText = MapCashError(_lastCashRejectReason);
            UpdateCashStatus();
            return;
        }

        if (amount > remaining)
        {
            KawaiiStudio.App.App.Log($"PAYMENT_BILL_REJECTED amount={amount} reason=overpayment remaining={remaining:0.00}");
            _lastCashRejectReason = "overpayment";
            CashErrorText = MapCashError(_lastCashRejectReason);
            UpdateCashStatus();
            return;
        }

        _cashAcceptor.SimulateBillInserted(amount);
    }

    private void HandleBillAccepted(object? sender, CashAcceptorEventArgs e)
    {
        RunOnUiThread(() =>
        {
            _lastCashRejectReason = null;
            CashErrorText = string.Empty;
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
                RecordCashPayment(GetCashAmountToRecord());
                MarkPaid();
            }
        });
    }

    private void HandleBillRejected(object? sender, CashAcceptorEventArgs e)
    {
        RunOnUiThread(() =>
        {
            var reason = string.IsNullOrWhiteSpace(e.Reason) ? "unknown" : e.Reason;
            KawaiiStudio.App.App.Log($"PAYMENT_BILL_REJECTED amount={e.Amount} reason={reason}");
            _lastCashRejectReason = reason;
            CashErrorText = MapCashError(reason);
            UpdateCashStatus();
        });
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
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
        _simulateVisaDebitCommand.RaiseCanExecuteChanged();
        _simulateMastercardCommand.RaiseCanExecuteChanged();
        _simulateAmexCommand.RaiseCanExecuteChanged();
        _simulateDiscoverCommand.RaiseCanExecuteChanged();
        _simulateJcbCommand.RaiseCanExecuteChanged();
        _simulateUnionPayCommand.RaiseCanExecuteChanged();
        _simulateInteracCommand.RaiseCanExecuteChanged();
        _simulateCardOptionCommand.RaiseCanExecuteChanged();
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

    private void ResetTransactionTracking()
    {
        _cashTransactionLogged = false;
        _cardTransactionLogged = false;
    }

    private decimal GetCashAmountToRecord()
    {
        var cashInserted = _session.Current.CashInserted;
        if (cashInserted <= 0m)
        {
            return 0m;
        }

        var total = _session.Current.PriceTotal;
        if (total <= 0m)
        {
            total = CalculateTotalPrice();
        }

        if (total <= 0m)
        {
            return cashInserted;
        }

        return cashInserted > total ? total : cashInserted;
    }

    private void RecordCashPayment(decimal amount)
    {
        if (_cashTransactionLogged || amount <= 0m)
        {
            return;
        }

        _cashTransactionLogged = true;
        _ = _financeTracking.RecordTransactionAsync(
            _session.Current,
            "cash",
            amount,
            cancellationToken: CancellationToken.None);
    }

    private void RecordCardPayment(decimal amount, string? paymentIntentId = null)
    {
        if (_cardTransactionLogged || amount <= 0m)
        {
            return;
        }

        _cardTransactionLogged = true;
        _ = _financeTracking.RecordTransactionAsync(
            _session.Current,
            "card",
            amount,
            stripePaymentIntentId: paymentIntentId,
            cancellationToken: CancellationToken.None);
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

    private static string FormatCashReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "unknown";
        }

        return reason.Replace('_', ' ');
    }

    private static string MapCashError(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        if (reason.StartsWith("fault_0x", StringComparison.OrdinalIgnoreCase))
        {
            var code = reason.Replace("fault_", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
            return $"Cash reader fault ({code}).";
        }

        return reason switch
        {
            "intake_disabled" => "Cash reader intake disabled.",
            "not_connected" => "Cash reader not connected.",
            "manual_insert_disabled" => "Cash reader disabled.",
            "overpayment" => "Bill exceeds remaining balance.",
            "unsupported_denomination" => "Unsupported denomination.",
            "invalid_amount" => "Invalid bill amount.",
            "no_balance_due" => "No balance due.",
            "rejected" => "Bill rejected.",
            "error_0x11" => "Cash reader error.",
            _ => string.Empty
        };
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

    public IReadOnlyList<CardTestOption> CardTestOptions { get; }
    public IReadOnlyList<CardTestOption> StandardCardTests { get; }
    public IReadOnlyList<CardTestOption> SuccessCardTests { get; }
    public IReadOnlyList<CardTestOption> ErrorCardTests { get; }

    private static IReadOnlyList<CardTestOption> BuildCardTestOptions()
    {
        return new List<CardTestOption>
        {
            new("Standard", "Visa 4242", "4242424242424242"),
            new("Standard", "Visa debit 5556", "4000056655665556"),
            new("Standard", "Mastercard 4444", "5555555555554444"),
            new("Standard", "Mastercard debit 8210", "5200828282828210"),
            new("Standard", "Mastercard prepaid 5100", "5105105105105100"),
            new("Standard", "Amex 0005", "378282246310005"),
            new("Standard", "Amex 8431", "371449635398431"),
            new("Standard", "Discover 1117", "6011111111111117"),
            new("Standard", "Discover 9424", "6011000990139424"),
            new("Standard", "Diners 0004", "3056930009020004"),
            new("Standard", "Diners (14) 1667", "36227206271667"),
            new("Standard", "JCB 0505", "3566002020360505"),
            new("Standard", "UnionPay 0005", "6200000000000005"),
            new("Standard", "Interac 1933", "4506445006931933"),
            new("Standard", "EFTPOS AU debit 0978", "6280000360000978"),
            new("Standard", "EFTPOS AU Visa debit 0001", "4000050360000001"),
            new("Standard", "EFTPOS AU MC debit 0080", "5555050360000080"),
            new("Standard", "Cartes Bancaires Visa 1001", "4000002500001001"),
            new("Standard", "Cartes Bancaires MC 1001", "5555552500001001"),
            new("Standard", "Girocard 6877", "4711009900000316877"),
            new("Success", "Offline PIN", "4001007020000002"),
            new("Success", "Offline PIN SCA retry", "4000008260000075"),
            new("Success", "Online PIN", "4001000360000005"),
            new("Success", "Online PIN SCA retry", "4000002760000008"),
            new("Error", "Declined", "4000000000000002"),
            new("Error", "Insufficient funds", "4000000000009995"),
            new("Error", "Lost card", "4000000000009987"),
            new("Error", "Stolen card", "4000000000009979"),
            new("Error", "Expired card", "4000000000000069"),
            new("Error", "Processing error", "4000000000000119"),
            new("Error", "Refund fail (JS only)", "4000000000005126")
        };
    }

    public sealed class CardTestOption
    {
        public CardTestOption(string category, string label, string cardNumber)
        {
            Category = category;
            Label = label;
            CardNumber = cardNumber;
        }

        public string Category { get; }
        public string Label { get; }
        public string CardNumber { get; }
    }
}
