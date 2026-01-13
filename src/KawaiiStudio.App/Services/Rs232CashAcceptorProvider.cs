using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public sealed class Rs232CashAcceptorProvider : ICashAcceptorProvider
{
    private const int BaudRate = 9600;
    private const int ReadTimeoutMilliseconds = 500;
    private const int WriteTimeoutMilliseconds = 500;
    private const int PollIntervalMilliseconds = 300;

    private const byte CommandAck = 0x02;
    private const byte CommandEnable = 0x3E;
    private const byte CommandDisable = 0x5E;
    private const byte CommandPoll = 0x0C;
    private const byte CommandReject = 0x0F;

    private const byte EventPowerUp = 0x80;
    private const byte EventHandshakeComplete = 0x8F;
    private const byte EventOnlineAlternative = 0x3F;
    private const byte EventBillValidated = 0x81;
    private const byte EventStacked = 0x10;
    private const byte EventRejected = 0x11;

    private static readonly IReadOnlyDictionary<byte, int> BillTypeMap = new Dictionary<byte, int>
    {
        { 0x40, 5 },
        { 0x41, 10 },
        { 0x42, 20 }
    };

    private readonly string _portName;
    private readonly HashSet<int> _allowedBills;
    private readonly bool _logAllBytes;
    private readonly object _connectLock = new();
    private readonly object _writeLock = new();
    private readonly object _stateLock = new();
    private Task<bool>? _connectTask;
    private bool _connecting;
    private TaskCompletionSource<bool>? _connectTcs;
    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private Timer? _pollTimer;
    private bool _onlineHandshakeSent;
    private bool _expectBillType;
    private bool _handshakeComplete;
    private bool _acceptingEnabled;
    private bool? _lastAppliedAccepting;
    private int? _pendingAmount;
    private string? _pendingRejectReason;
    private bool _pendingAccepted;
    private decimal _remainingAmount;
    private decimal? _lastLoggedRemaining;

    public Rs232CashAcceptorProvider(string portName, IEnumerable<int>? allowedBills = null, bool logAllBytes = false)
    {
        _portName = portName;
        _allowedBills = NormalizeAllowedBills(allowedBills);
        _logAllBytes = logAllBytes;
    }

    public bool IsConnected { get; private set; }

    public event EventHandler<CashAcceptorEventArgs>? BillAccepted;
    public event EventHandler<CashAcceptorEventArgs>? BillRejected;

    private bool IsPortOpen => _port is not null && _port.IsOpen;

    public Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_connectLock)
        {
            if (IsConnected || IsPortOpen)
            {
                return Task.FromResult(true);
            }

            if (_connecting && _connectTask is not null)
            {
                return _connectTask;
            }

            _connecting = true;
            _connectTask = Task.Run(() =>
            {
                try
                {
                    return ConnectInternal(cancellationToken);
                }
                finally
                {
                    lock (_connectLock)
                    {
                        _connecting = false;
                    }
                }
            }, cancellationToken);

            return _connectTask;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        return Task.Run(DisconnectInternal, cancellationToken);
    }

    public void SimulateBillInserted(int amount)
    {
        BillRejected?.Invoke(this, new CashAcceptorEventArgs(amount, "manual_insert_disabled"));
    }

    public void UpdateRemainingAmount(decimal amount)
    {
        lock (_stateLock)
        {
            _remainingAmount = amount < 0m ? 0m : amount;
            _acceptingEnabled = _remainingAmount > 0m;
        }

        LogRemainingAmount();
        ApplyAcceptanceState(force: false);
    }

    private bool ConnectInternal(CancellationToken cancellationToken)
    {
        try
        {
            KawaiiStudio.App.App.Log($"CASH_CONNECT port={_portName}");
            var port = new SerialPort(_portName, BaudRate, Parity.Even, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = ReadTimeoutMilliseconds,
                WriteTimeout = WriteTimeoutMilliseconds
            };

            port.Open();
            _port = port;
            ResetProtocolState();
            IsConnected = false;
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_readCts.Token));
            _pollTimer = new Timer(_ => SendByte(CommandPoll), null, PollIntervalMilliseconds, PollIntervalMilliseconds);

            ApplyAcceptanceState(force: true);

            if (!cancellationToken.CanBeCanceled)
            {
                return true;
            }

            var connected = WaitForConnection(cancellationToken);
            if (!connected)
            {
                DisconnectInternal();
            }

            return connected;
        }
        catch
        {
            KawaiiStudio.App.App.Log("CASH_CONNECT_FAILED");
            DisconnectInternal();
            return false;
        }
    }

    private void DisconnectInternal()
    {
        if (_port is null)
        {
            return;
        }

        KawaiiStudio.App.App.Log("CASH_DISCONNECT");
        try
        {
            SendByte(CommandDisable);
        }
        catch
        {
            // Ignore shutdown errors.
        }

        _pollTimer?.Dispose();
        _pollTimer = null;

        _readCts?.Cancel();
        _readCts = null;

        if (_port is not null)
        {
            try
            {
                _port.Close();
            }
            catch
            {
                // Ignore close errors.
            }

            _port.Dispose();
            _port = null;
        }

        IsConnected = false;
        ClearConnectWaiter(false);
        ResetProtocolState();
    }

    private void ResetProtocolState()
    {
        lock (_stateLock)
        {
            _onlineHandshakeSent = false;
            _expectBillType = false;
            _handshakeComplete = false;
            _lastAppliedAccepting = null;
            _pendingAmount = null;
            _pendingRejectReason = null;
            _pendingAccepted = false;
        }

        _lastLoggedRemaining = null;
    }

    private bool WaitForConnection(CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool> waiter;
        lock (_stateLock)
        {
            if (IsConnected)
            {
                return true;
            }

            waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectTcs = waiter;
        }

        using var registration = cancellationToken.Register(() => waiter.TrySetResult(false));
        var result = waiter.Task.GetAwaiter().GetResult();

        lock (_stateLock)
        {
            if (ReferenceEquals(_connectTcs, waiter))
            {
                _connectTcs = null;
            }
        }

        return result;
    }

    private void MarkConnected()
    {
        if (IsConnected)
        {
            return;
        }

        IsConnected = true;
        KawaiiStudio.App.App.Log("CASH_CONNECTED");
        ClearConnectWaiter(true);
    }

    private void ClearConnectWaiter(bool connected)
    {
        TaskCompletionSource<bool>? waiter;
        lock (_stateLock)
        {
            waiter = _connectTcs;
            _connectTcs = null;
        }

        waiter?.TrySetResult(connected);
    }

    private void ReadLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            byte value;
            try
            {
                var port = _port;
                if (port is null || !port.IsOpen)
                {
                    return;
                }

                var read = port.ReadByte();
                if (read < 0)
                {
                    continue;
                }

                value = (byte)read;
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch
            {
                return;
            }

            HandleByte(value);
        }
    }

    private void HandleByte(byte value)
    {
        LogRx(value);

        if (IsConnectionByte(value))
        {
            MarkConnected();
        }

        if (IsOnlineByte(value))
        {
            HandleOnline(value);
            return;
        }

        if (value == EventPowerUp)
        {
            lock (_stateLock)
            {
                _onlineHandshakeSent = false;
                _handshakeComplete = false;
                _expectBillType = false;
                _pendingAmount = null;
                _pendingRejectReason = null;
                _pendingAccepted = false;
            }
            KawaiiStudio.App.App.Log("CASH_HANDSHAKE_START");
            SendByte(CommandAck);
            ApplyAcceptanceState(force: true);
            return;
        }

        if (value == EventHandshakeComplete)
        {
            if (!_handshakeComplete)
            {
                _handshakeComplete = true;
                KawaiiStudio.App.App.Log("CASH_HANDSHAKE_OK");
            }

            ApplyAcceptanceState(force: true);
            return;
        }

        if (value == EventBillValidated)
        {
            lock (_stateLock)
            {
                _expectBillType = true;
                _pendingAmount = null;
                _pendingRejectReason = null;
                _pendingAccepted = false;
            }
            KawaiiStudio.App.App.Log("CASH_BILL_VALIDATED");
            return;
        }

        if (IsBillType(value))
        {
            HandleBillType(value);
            return;
        }

        if (value == EventStacked)
        {
            HandleStacked();
            return;
        }

        if (value == EventRejected)
        {
            HandleRejected();
            return;
        }

        if (value >= 0x20 && value <= 0x2A)
        {
            HandleFault(value);
            return;
        }

        if (IsStatusByte(value))
        {
            HandleStatus(value);
            return;
        }
    }

    private void HandleBillType(byte value)
    {
        int? amount = null;
        string? rejectReason = null;
        bool shouldAccept;
        bool directEscrow;
        bool ignoreEscrow;

        lock (_stateLock)
        {
            if (_pendingAccepted)
            {
                ignoreEscrow = true;
                directEscrow = false;
                shouldAccept = false;
                _expectBillType = false;
            }
            else
            {
                ignoreEscrow = false;
                directEscrow = !_expectBillType;
                _expectBillType = false;

                if (BillTypeMap.TryGetValue(value, out var mappedAmount))
                {
                    amount = mappedAmount;
                    if (!_acceptingEnabled)
                    {
                        rejectReason = "intake_disabled";
                    }
                    else if (amount <= 0)
                    {
                        rejectReason = "invalid_amount";
                    }
                    else if (_allowedBills.Count > 0 && !_allowedBills.Contains(amount.Value))
                    {
                        rejectReason = "unsupported_denomination";
                    }
                    else if (_remainingAmount <= 0m)
                    {
                        rejectReason = "no_balance_due";
                    }
                    else if (amount.Value > _remainingAmount)
                    {
                        rejectReason = "overpayment";
                    }
                }
                else
                {
                    rejectReason = "unsupported_denomination";
                }

                _pendingAmount = amount;
                _pendingRejectReason = rejectReason;
                _pendingAccepted = rejectReason is null && amount.HasValue && amount.Value > 0;
                shouldAccept = _pendingAccepted;
            }
        }

        if (ignoreEscrow)
        {
            KawaiiStudio.App.App.Log("CASH_ESCROW_IGNORED reason=pending");
            return;
        }

        if (directEscrow)
        {
            KawaiiStudio.App.App.Log("CASH_ESCROW_DIRECT");
        }

        if (shouldAccept)
        {
            KawaiiStudio.App.App.Log($"CASH_ESCROW_DECISION action=accept amount={amount}");
            SendByte(CommandAck);
        }
        else
        {
            var reason = string.IsNullOrWhiteSpace(rejectReason) ? "unknown" : rejectReason;
            KawaiiStudio.App.App.Log($"CASH_ESCROW_DECISION action=reject amount={amount} reason={reason}");
            SendByte(CommandReject);
        }
    }

    private void HandleStacked()
    {
        int? amount;
        bool accepted;
        lock (_stateLock)
        {
            amount = _pendingAmount;
            _pendingAmount = null;
            _pendingRejectReason = null;
            accepted = _pendingAccepted;
            _pendingAccepted = false;
        }

        KawaiiStudio.App.App.Log($"CASH_STACKED amount={amount}");
        if (!accepted || amount is null || amount <= 0)
        {
            return;
        }

        BillAccepted?.Invoke(this, new CashAcceptorEventArgs(amount.Value));
    }

    private void HandleRejected()
    {
        int amount;
        string? reason;
        bool accepted;
        lock (_stateLock)
        {
            amount = _pendingAmount ?? 0;
            reason = _pendingRejectReason ?? "rejected";
            _pendingAmount = null;
            _pendingRejectReason = null;
            accepted = _pendingAccepted;
            _pendingAccepted = false;
        }

        if (accepted)
        {
            reason = "error_0x11";
            KawaiiStudio.App.App.Log($"CASH_ERROR_GENERIC amount={amount} reason={reason}");
        }
        else
        {
            KawaiiStudio.App.App.Log($"CASH_REJECTED amount={amount} reason={reason}");
        }
        BillRejected?.Invoke(this, new CashAcceptorEventArgs(amount, reason));
    }

    private void HandleFault(byte code)
    {
        KawaiiStudio.App.App.Log($"CASH_FAULT code=0x{code:X2}");
        lock (_stateLock)
        {
            _acceptingEnabled = false;
            _expectBillType = false;
            _pendingAmount = null;
            _pendingRejectReason = null;
            _pendingAccepted = false;
        }

        ApplyAcceptanceState(force: true);
        BillRejected?.Invoke(this, new CashAcceptorEventArgs(0, $"fault_0x{code:X2}"));
    }

    private void ApplyAcceptanceState(bool force)
    {
        bool accepting;
        lock (_stateLock)
        {
            accepting = _acceptingEnabled;
            if (!force && _lastAppliedAccepting.HasValue && _lastAppliedAccepting.Value == accepting)
            {
                return;
            }

            _lastAppliedAccepting = accepting;
        }

        if (!IsPortOpen)
        {
            return;
        }

        KawaiiStudio.App.App.Log($"CASH_ACCEPTING enabled={accepting}");
        SendByte(accepting ? CommandEnable : CommandDisable);
    }

    private void SendByte(byte value)
    {
        var port = _port;
        if (port is null || !port.IsOpen)
        {
            return;
        }

        lock (_writeLock)
        {
            port.Write(new[] { value }, 0, 1);
        }

        LogTx(value);
    }

    private static bool IsBillType(byte value)
    {
        return value >= 0x40 && value <= 0x44;
    }

    private static bool IsStatusByte(byte value)
    {
        return value == CommandEnable || value == CommandDisable;
    }

    private static bool IsOnlineByte(byte value)
    {
        return value == CommandEnable || value == EventOnlineAlternative;
    }

    private static bool IsConnectionByte(byte value)
    {
        return value == EventPowerUp
            || value == EventHandshakeComplete
            || value == EventBillValidated
            || value == EventStacked
            || value == EventRejected
            || IsBillType(value)
            || IsOnlineByte(value)
            || IsStatusByte(value)
            || (value >= 0x20 && value <= 0x2A);
    }

    private static bool ShouldLogRxByte(byte value)
    {
        return value == EventPowerUp
            || value == EventHandshakeComplete
            || IsOnlineByte(value)
            || value == EventBillValidated
            || value == EventStacked
            || value == EventRejected
            || IsBillType(value)
            || IsStatusByte(value)
            || (value >= 0x20 && value <= 0x2A);
    }

    private void HandleOnline(byte value)
    {
        bool shouldHandshake;
        lock (_stateLock)
        {
            shouldHandshake = !_onlineHandshakeSent && !_pendingAccepted && !_expectBillType;
            if (shouldHandshake)
            {
                _onlineHandshakeSent = true;
                _handshakeComplete = true;
            }
        }

        if (shouldHandshake)
        {
            KawaiiStudio.App.App.Log("CASH_ONLINE");
            SendByte(CommandAck);
            ApplyAcceptanceState(force: true);
        }

        if (value == CommandEnable)
        {
            HandleStatus(value);
        }
    }

    private void HandleStatus(byte value)
    {
        lock (_stateLock)
        {
            _lastAppliedAccepting = value == CommandEnable;
        }

        var status = value == CommandEnable ? "enabled" : "disabled";
        KawaiiStudio.App.App.Log($"CASH_STATUS {status}");
    }

    private void LogRx(byte value)
    {
        if (!_logAllBytes && !ShouldLogRxByte(value))
        {
            return;
        }

        KawaiiStudio.App.App.Log($"CASH_RX byte=0x{value:X2}");
    }

    private void LogTx(byte value)
    {
        if (!_logAllBytes && value == CommandPoll)
        {
            return;
        }

        KawaiiStudio.App.App.Log($"CASH_TX byte=0x{value:X2}");
    }

    private void LogRemainingAmount()
    {
        decimal remaining;
        bool accepting;
        lock (_stateLock)
        {
            remaining = _remainingAmount;
            accepting = _acceptingEnabled;
        }

        if (_logAllBytes || !_lastLoggedRemaining.HasValue || _lastLoggedRemaining.Value != remaining)
        {
            _lastLoggedRemaining = remaining;
            KawaiiStudio.App.App.Log($"CASH_REMAINING amount={remaining:0.00} accepting={accepting}");
        }
    }

    private static HashSet<int> NormalizeAllowedBills(IEnumerable<int>? allowedBills)
    {
        if (allowedBills is null)
        {
            return new HashSet<int>();
        }

        var results = new HashSet<int>();
        foreach (var bill in allowedBills)
        {
            if (bill > 0)
            {
                results.Add(bill);
            }
        }

        return results;
    }
}
