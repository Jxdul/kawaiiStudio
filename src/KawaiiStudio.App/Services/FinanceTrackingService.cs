using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class FinanceTrackingService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;

    public FinanceTrackingService(SettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = new HttpClient
        {
            Timeout = DefaultTimeout
        };
    }

    public async Task<bool> RecordTransactionAsync(
        SessionState session,
        string paymentMethod,
        decimal amount,
        string? transactionId = null,
        string? stripePaymentIntentId = null,
        CancellationToken cancellationToken = default)
    {
        if (_settings.TestMode)
        {
            return false;
        }

        if (session is null)
        {
            return false;
        }

        var normalizedMethod = NormalizePaymentMethod(paymentMethod);
        if (string.IsNullOrWhiteSpace(normalizedMethod))
        {
            return false;
        }

        var cents = (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (cents <= 0)
        {
            return false;
        }

        var baseUrl = ResolveBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            App.Log("FINANCE_TRACKING_SKIPPED reason=missing_base_url");
            return false;
        }

        var boothId = NormalizeBoothId(_settings.BoothId);
        if (string.IsNullOrWhiteSpace(boothId))
        {
            App.Log("FINANCE_TRACKING_SKIPPED reason=missing_booth_id");
            return false;
        }

        var sessionId = BuildSessionId(session, boothId);
        var finalTransactionId = string.IsNullOrWhiteSpace(transactionId)
            ? BuildTransactionId(session, boothId, normalizedMethod)
            : transactionId.Trim();

        var payload = new
        {
            boothId,
            sessionId,
            amount_cents = cents,
            currency = "cad",
            paymentMethod = normalizedMethod,
            transactionId = finalTransactionId,
            stripePaymentIntentId
        };

        try
        {
            var url = $"{baseUrl}/api/transactions";
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                App.Log($"FINANCE_TRACKING_FAILED status={(int)response.StatusCode}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            App.Log($"FINANCE_TRACKING_FAILED error={ex.GetType().Name}");
            return false;
        }
    }

    private string ResolveBaseUrl()
    {
        var baseUrl = _settings.GetValue("FINANCE_BASE_URL", string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return baseUrl.TrimEnd('/');
        }

        baseUrl = _settings.GetValue("UPLOAD_BASE_URL", string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return baseUrl.TrimEnd('/');
        }

        baseUrl = _settings.GetValue("STRIPE_TERMINAL_BASE_URL", string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return baseUrl.TrimEnd('/');
        }

        return string.Empty;
    }

    private static string NormalizePaymentMethod(string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            return string.Empty;
        }

        var normalized = paymentMethod.Trim().ToLowerInvariant();
        return normalized is "cash" or "card" ? normalized : string.Empty;
    }

    private static string BuildSessionId(SessionState session, string boothId)
    {
        var baseId = string.IsNullOrWhiteSpace(session.SessionId) ? "session" : session.SessionId;
        if (string.IsNullOrWhiteSpace(boothId))
        {
            return baseId;
        }

        var prefix = $"{boothId}_";
        if (baseId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return baseId;
        }

        return $"{boothId}_{baseId}";
    }

    private static string BuildTransactionId(SessionState session, string boothId, string paymentMethod)
    {
        var baseId = string.IsNullOrWhiteSpace(session.SessionId) ? "session" : session.SessionId;
        var start = session.StartTime == default ? DateTime.UtcNow : session.StartTime;
        var stamp = start.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return $"{boothId}_{baseId}_{stamp}_{paymentMethod}";
    }

    private static string NormalizeBoothId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var ch in raw.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
            {
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append('_');
            }
        }

        return builder.ToString();
    }
}
