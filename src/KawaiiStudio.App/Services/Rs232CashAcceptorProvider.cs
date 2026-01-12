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
    private const int PollIntervalMilliseconds = 500;

    private const byte CommandAck = 0x02;
    private const byte CommandEnable = 0x3E;
    private const byte CommandDisable = 0x5E;
    private const byte CommandPoll = 0x0C;
    private const byte CommandReject = 0x0F;

    private const byte EventPowerUp = 0x80;
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
    private readonly object _writeLock = new();
    private readonly object _stateLock = new();
    private SerialPort? _port;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private Timer? _pollTimer;
    private bool _expectBillType;
    private int? _pendingAmount;
    private string? _pendingRejectReason;
    private decimal _remainingAmount;

    public Rs232CashAcceptorProvider(string portName)
    {
        _portName = portName;
    }

    public bool IsConnected { get; private set; }

    public event EventHandler<CashAcceptorEventArgs>? BillAccepted;
    public event EventHandler<CashAcceptorEventArgs>? BillRejected;

    public Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return Task.FromResult(true);
        }

        return Task.Run(ConnectInternal, cancellationToken);
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
        }
    }

    private bool ConnectInternal()
    {
        try
        {
            var port = new SerialPort(_portName, BaudRate, Parity.Even, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = ReadTimeoutMilliseconds,
                WriteTimeout = WriteTimeoutMilliseconds
            };

            port.Open();
            _port = port;
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoop(_readCts.Token));
            _pollTimer = new Timer(_ => SendByte(CommandPoll), null, PollIntervalMilliseconds, PollIntervalMilliseconds);
            IsConnected = true;

            SendByte(CommandEnable);
            return true;
        }
        catch
        {
            DisconnectInternal();
            return false;
        }
    }

    private void DisconnectInternal()
    {
        if (!IsConnected && _port is null)
        {
            return;
        }

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
        ResetState();
    }

    private void ResetState()
    {
        lock (_stateLock)
        {
            _expectBillType = false;
            _pendingAmount = null;
            _pendingRejectReason = null;
        }
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
        if (value == EventPowerUp)
        {
            SendByte(CommandAck);
            SendByte(CommandEnable);
            return;
        }

        if (value == EventBillValidated)
        {
            lock (_stateLock)
            {
                _expectBillType = true;
            }
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
            KawaiiStudio.App.App.Log($"CASH_FAULT code=0x{value:X2}");
            SendByte(CommandDisable);
            return;
        }
    }

    private void HandleBillType(byte value)
    {
        int amount = 0;
        string? rejectReason = null;

        lock (_stateLock)
        {
            if (!_expectBillType)
            {
                return;
            }

            _expectBillType = false;
            if (BillTypeMap.TryGetValue(value, out var mappedAmount))
            {
                amount = mappedAmount;
                if (amount <= 0)
                {
                    rejectReason = "invalid_amount";
                }
                else if (_remainingAmount <= 0m)
                {
                    rejectReason = "no_balance_due";
                }
                else if (amount > _remainingAmount)
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
        }

        if (rejectReason is null)
        {
            SendByte(CommandAck);
        }
        else
        {
            SendByte(CommandReject);
        }
    }

    private void HandleStacked()
    {
        int? amount;
        lock (_stateLock)
        {
            amount = _pendingAmount;
            _pendingAmount = null;
            _pendingRejectReason = null;
        }

        if (amount is null || amount <= 0)
        {
            return;
        }

        BillAccepted?.Invoke(this, new CashAcceptorEventArgs(amount.Value));
    }

    private void HandleRejected()
    {
        int amount;
        string? reason;
        lock (_stateLock)
        {
            amount = _pendingAmount ?? 0;
            reason = _pendingRejectReason ?? "rejected";
            _pendingAmount = null;
            _pendingRejectReason = null;
        }

        BillRejected?.Invoke(this, new CashAcceptorEventArgs(amount, reason));
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
    }

    private static bool IsBillType(byte value)
    {
        return value >= 0x40 && value <= 0x44;
    }
}
