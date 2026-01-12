using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public sealed class StripeTerminalCardPaymentProvider : ICardPaymentProvider, IStripeTerminalTestProvider
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;
    private string? _readerId;
    private string? _locationId;
    private string? _paymentIntentId;
    private decimal _pendingAmount;
    private bool _inProgress;

    public StripeTerminalCardPaymentProvider(SettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = new HttpClient
        {
            Timeout = DefaultTimeout
        };
    }

    public bool IsConnected { get; private set; }

    public event EventHandler<CardPaymentEventArgs>? PaymentApproved;
    public event EventHandler<CardPaymentEventArgs>? PaymentDeclined;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return true;
        }

        var baseUrl = GetBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        _readerId = GetReaderId();
        _locationId = GetLocationId();
        IsConnected = true;

        if (string.IsNullOrWhiteSpace(_readerId))
        {
            var ok = await EnsureSimulatedReaderAsync(cancellationToken);
            IsConnected = ok;
            return ok;
        }

        if (string.IsNullOrWhiteSpace(_readerId))
        {
            IsConnected = false;
            return false;
        }

        return true;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = false;
        _readerId = null;
        _locationId = null;
        _paymentIntentId = null;
        _pendingAmount = 0m;
        _inProgress = false;
        return Task.CompletedTask;
    }

    public async Task<bool> StartPaymentAsync(decimal amount, CancellationToken cancellationToken)
    {
        if (amount <= 0m)
        {
            return false;
        }

        if (!IsConnected && !await ConnectAsync(cancellationToken))
        {
            return false;
        }

        _readerId = GetReaderId();
        if (string.IsNullOrWhiteSpace(_readerId))
        {
            if (await EnsureSimulatedReaderAsync(cancellationToken))
            {
                _readerId = GetReaderId();
            }
        }

        if (string.IsNullOrWhiteSpace(_readerId))
        {
            return false;
        }

        var paymentIntentId = await CreatePaymentIntentAsync(amount, cancellationToken);
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            return false;
        }

        _pendingAmount = amount;
        _paymentIntentId = paymentIntentId;
        _inProgress = true;

        using var processResult = await ProcessPaymentAsync(_readerId, paymentIntentId, cancellationToken);
        if (processResult is null)
        {
            return false;
        }

        var actionStatus = GetActionStatus(processResult.RootElement);
        if (string.Equals(actionStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var message = GetActionFailure(processResult.RootElement) ?? "card_declined";
            HandleDeclined(message);
            return false;
        }

        if (string.Equals(actionStatus, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return await TryCaptureAndCompleteAsync(cancellationToken);
        }

        return true;
    }

    public Task CancelAsync(CancellationToken cancellationToken)
    {
        _inProgress = false;
        _paymentIntentId = null;
        _pendingAmount = 0m;
        return Task.CompletedTask;
    }

    public void SimulateApprove()
    {
        _ = SimulatePaymentAsync("4242424242424242", CancellationToken.None);
    }

    public void SimulateDecline(string? message = null)
    {
        _ = SimulatePaymentAsync("4000000000000002", CancellationToken.None);
    }

    public async Task<bool> SimulatePaymentAsync(string cardNumber, CancellationToken cancellationToken)
    {
        if (!_inProgress || string.IsNullOrWhiteSpace(_paymentIntentId))
        {
            return false;
        }

        _readerId ??= GetReaderId();
        if (string.IsNullOrWhiteSpace(_readerId))
        {
            return false;
        }

        using var simulateResult = await SimulatePaymentInternalAsync(_readerId, cardNumber, cancellationToken);
        if (simulateResult is null)
        {
            return false;
        }

        var actionStatus = GetActionStatus(simulateResult.RootElement);
        if (string.Equals(actionStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var message = GetActionFailure(simulateResult.RootElement) ?? "card_declined";
            HandleDeclined(message);
            return false;
        }

        return await TryCaptureAndCompleteAsync(cancellationToken);
    }

    private async Task<bool> TryCaptureAndCompleteAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_paymentIntentId))
        {
            return false;
        }

        using var captureResult = await CapturePaymentIntentAsync(_paymentIntentId, cancellationToken);
        if (captureResult is null)
        {
            HandleDeclined("capture_failed");
            return false;
        }

        var status = GetPaymentIntentStatus(captureResult.RootElement);
        if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "requires_capture", StringComparison.OrdinalIgnoreCase))
        {
            HandleApproved();
            return true;
        }

        HandleDeclined(status ?? "payment_failed");
        return false;
    }

    private async Task<bool> EnsureSimulatedReaderAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_readerId))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(_locationId))
        {
            _locationId = await CreateLocationAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(_locationId))
        {
            return false;
        }

        _readerId = await RegisterReaderAsync(_locationId, cancellationToken);
        return !string.IsNullOrWhiteSpace(_readerId);
    }

    private async Task<string?> CreateLocationAsync(CancellationToken cancellationToken)
    {
        var payload = new
        {
            display_name = "KawaiiStudio",
            address = new
            {
                line1 = "1272 Valencia Street",
                city = "San Francisco",
                state = "CA",
                country = "US",
                postal_code = "94110"
            }
        };

        using var response = await PostJsonAsync("create_location", payload, cancellationToken);
        return response is null ? null : GetId(response.RootElement);
    }

    private async Task<string?> RegisterReaderAsync(string locationId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            location_id = locationId
        };

        using var response = await PostJsonAsync("register_reader", payload, cancellationToken);
        return response is null ? null : GetId(response.RootElement);
    }

    private async Task<string?> CreatePaymentIntentAsync(decimal amount, CancellationToken cancellationToken)
    {
        var cents = (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (cents <= 0)
        {
            return null;
        }

        var payload = new
        {
            amount = cents.ToString(CultureInfo.InvariantCulture)
        };

        using var response = await PostJsonAsync("create_payment_intent", payload, cancellationToken);
        return response is null ? null : GetId(response.RootElement);
    }

    private Task<JsonDocument?> ProcessPaymentAsync(string readerId, string paymentIntentId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            reader_id = readerId,
            payment_intent_id = paymentIntentId
        };

        return PostJsonAsync("process_payment", payload, cancellationToken);
    }

    private Task<JsonDocument?> SimulatePaymentInternalAsync(string readerId, string cardNumber, CancellationToken cancellationToken)
    {
        var payload = new
        {
            reader_id = readerId,
            card_number = cardNumber
        };

        return PostJsonAsync("simulate_payment", payload, cancellationToken);
    }

    private Task<JsonDocument?> CapturePaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            payment_intent_id = paymentIntentId
        };

        return PostJsonAsync("capture_payment_intent", payload, cancellationToken);
    }

    private async Task<JsonDocument?> PostJsonAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var baseUrl = GetBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var url = $"{baseUrl}/{path.TrimStart('/')}";
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            KawaiiStudio.App.App.Log($"STRIPE_TERMINAL_HTTP_FAIL url={path} status={(int)response.StatusCode}");
            return null;
        }

        return JsonDocument.Parse(body);
    }

    private static string? GetId(JsonElement root)
    {
        return root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
    }

    private static string? GetActionStatus(JsonElement root)
    {
        if (!root.TryGetProperty("action", out var action))
        {
            return null;
        }

        return action.TryGetProperty("status", out var status) ? status.GetString() : null;
    }

    private static string? GetActionFailure(JsonElement root)
    {
        if (!root.TryGetProperty("action", out var action))
        {
            return null;
        }

        if (action.TryGetProperty("failure_message", out var messageValue))
        {
            return messageValue.GetString();
        }

        if (action.TryGetProperty("failure_code", out var codeValue))
        {
            return codeValue.GetString();
        }

        return null;
    }

    private static string? GetPaymentIntentStatus(JsonElement root)
    {
        return root.TryGetProperty("status", out var status) ? status.GetString() : null;
    }

    private string GetBaseUrl()
    {
        return _settings.GetValue("STRIPE_TERMINAL_BASE_URL", "http://localhost:4242").Trim().TrimEnd('/');
    }

    private string? GetReaderId()
    {
        return string.IsNullOrWhiteSpace(_readerId)
            ? _settings.GetValue("STRIPE_TERMINAL_READER_ID", string.Empty).Trim()
            : _readerId;
    }

    private string? GetLocationId()
    {
        return string.IsNullOrWhiteSpace(_locationId)
            ? _settings.GetValue("STRIPE_TERMINAL_LOCATION_ID", string.Empty).Trim()
            : _locationId;
    }

    private void HandleApproved()
    {
        var amount = _pendingAmount;
        _pendingAmount = 0m;
        _paymentIntentId = null;
        _inProgress = false;
        PaymentApproved?.Invoke(this, new CardPaymentEventArgs(amount));
    }

    private void HandleDeclined(string? message)
    {
        var amount = _pendingAmount;
        _pendingAmount = 0m;
        _paymentIntentId = null;
        _inProgress = false;
        PaymentDeclined?.Invoke(this, new CardPaymentEventArgs(amount, message));
    }
}
