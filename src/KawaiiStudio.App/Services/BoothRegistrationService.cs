using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace KawaiiStudio.App.Services;

public sealed class BoothRegistrationService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20); // Increased from 15s for better reliability
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly int MaxRetryAttempts = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;
    private Timer? _heartbeatTimer;
    private bool _isRegistered;
    private DateTime _lastHeartbeatTime;
    private PerformanceCounter? _cpuCounter;
    private bool _metricsInitialized;

    public BoothRegistrationService(SettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = new HttpClient
        {
            Timeout = DefaultTimeout
        };
        InitializeMetrics();
    }

    public bool IsRegistered => _isRegistered;

    public async Task<(bool ok, string? error)> RegisterAsync(
        string? name = null,
        string? location = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            App.Log("BOOTH_REGISTRATION_SKIPPED reason=missing_base_url");
            return (false, "missing_base_url");
        }

        var boothId = NormalizeBoothId(_settings.BoothId);
        if (string.IsNullOrWhiteSpace(boothId))
        {
            App.Log("BOOTH_REGISTRATION_SKIPPED reason=missing_booth_id");
            return (false, "missing_booth_id");
        }

        var softwareVersion = GetSoftwareVersion();

        var payload = new
        {
            boothId,
            name = name?.Trim(),
            location = location?.Trim(),
            softwareVersion
        };

        try
        {
            var url = $"{baseUrl}/api/booth/register";
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                App.Log($"BOOTH_REGISTRATION_FAILED status=403 reason=booth_not_allowed");
                return (false, "booth_not_allowed");
            }

            if (!response.IsSuccessStatusCode)
            {
                App.Log($"BOOTH_REGISTRATION_FAILED status={(int)response.StatusCode}");
                return (false, $"http_error_{(int)response.StatusCode}");
            }

            _isRegistered = true;
            App.Log($"BOOTH_REGISTERED boothId={boothId}");
            return (true, null);
        }
        catch (TaskCanceledException)
        {
            App.Log("BOOTH_REGISTRATION_FAILED reason=timeout");
            return (false, "timeout");
        }
        catch (Exception ex)
        {
            App.Log($"BOOTH_REGISTRATION_FAILED error={ex.GetType().Name}");
            return (false, ex.GetType().Name);
        }
    }

    public async Task<(bool ok, string? error)> SendHeartbeatAsync(
        bool includeMetrics = true,
        CancellationToken cancellationToken = default)
    {
        if (_settings.TestMode)
        {
            return (true, null);
        }

        if (!_isRegistered)
        {
            App.Log("BOOTH_HEARTBEAT_SKIPPED reason=not_registered");
            return (false, "not_registered");
        }

        var baseUrl = ResolveBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            App.Log("BOOTH_HEARTBEAT_SKIPPED reason=missing_base_url");
            return (false, "missing_base_url");
        }

        var boothId = NormalizeBoothId(_settings.BoothId);
        if (string.IsNullOrWhiteSpace(boothId))
        {
            App.Log("BOOTH_HEARTBEAT_SKIPPED reason=missing_booth_id");
            return (false, "missing_booth_id");
        }

        // Retry with exponential backoff
        for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromMilliseconds(
                    InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1)
                );
                App.Log($"BOOTH_HEARTBEAT_RETRY attempt={attempt + 1}/{MaxRetryAttempts} delay={delay.TotalSeconds}s");
                await Task.Delay(delay, cancellationToken);
            }

            object? metrics = null;
            if (includeMetrics)
            {
                metrics = CollectDeviceMetrics();
            }

            var payload = new
            {
                boothId,
                metrics
            };

            try
            {
                var url = $"{baseUrl}/api/booth/heartbeat";
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var startTime = DateTime.UtcNow;
                using var response = await _httpClient.PostAsync(url, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // Don't retry on client errors (4xx), only on server errors (5xx) and timeouts
                    if (response.StatusCode >= System.Net.HttpStatusCode.BadRequest && 
                        response.StatusCode < System.Net.HttpStatusCode.InternalServerError)
                    {
                        App.Log($"BOOTH_HEARTBEAT_FAILED status={(int)response.StatusCode} (no_retry)");
                        return (false, $"http_error_{(int)response.StatusCode}");
                    }
                    
                    // Server error - will retry
                    App.Log($"BOOTH_HEARTBEAT_FAILED status={(int)response.StatusCode} attempt={attempt + 1}/{MaxRetryAttempts}");
                    continue;
                }

                _lastHeartbeatTime = DateTime.UtcNow;
                var latency = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                App.Log($"BOOTH_HEARTBEAT_OK latency={latency}ms");
                return (true, null);
            }
            catch (TaskCanceledException)
            {
                // Timeout - will retry
                App.Log($"BOOTH_HEARTBEAT_FAILED reason=timeout attempt={attempt + 1}/{MaxRetryAttempts}");
                if (attempt == MaxRetryAttempts - 1)
                {
                    return (false, "timeout");
                }
                continue;
            }
            catch (Exception ex)
            {
                // Network errors - will retry
                App.Log($"BOOTH_HEARTBEAT_FAILED error={ex.GetType().Name} attempt={attempt + 1}/{MaxRetryAttempts}");
                if (attempt == MaxRetryAttempts - 1)
                {
                    return (false, ex.GetType().Name);
                }
                continue;
            }
        }

        // All retries exhausted
        return (false, "max_retries_exceeded");
    }

    public async Task<(bool ok, bool isAllowed, string? error)> CheckBoothAllowedAsync(
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return (false, false, "missing_base_url");
        }

        var boothId = NormalizeBoothId(_settings.BoothId);
        if (string.IsNullOrWhiteSpace(boothId))
        {
            return (false, false, "missing_booth_id");
        }

        try
        {
            var url = $"{baseUrl}/api/booth/{Uri.EscapeDataString(boothId)}/status";
            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return (true, false, "booth_not_allowed");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (true, true, "booth_not_registered");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (false, false, $"http_error_{(int)response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            var ok = json.RootElement.TryGetProperty("ok", out var okValue) && okValue.GetBoolean();

            return (ok, ok, null);
        }
        catch (TaskCanceledException)
        {
            return (false, false, "timeout");
        }
        catch (Exception ex)
        {
            return (false, false, ex.GetType().Name);
        }
    }

    public void StartHeartbeatTimer()
    {
        if (_heartbeatTimer != null)
        {
            return;
        }

        if (_settings.TestMode)
        {
            App.Log("BOOTH_HEARTBEAT_TIMER_SKIPPED reason=test_mode");
            return;
        }

        // Send an immediate heartbeat to ensure the server knows the booth is online
        // right after registration, before starting the periodic timer
        _ = SendImmediateHeartbeatAsync();

        // Use System.Threading.Timer instead of DispatcherTimer to ensure
        // heartbeats continue even when the app is idle or UI thread is blocked
        // Start with zero delay so the first heartbeat is sent immediately,
        // then continue every HeartbeatInterval
        _heartbeatTimer = new Timer(
            OnHeartbeatTimerTick,
            null,
            TimeSpan.Zero,
            HeartbeatInterval);
        App.Log("BOOTH_HEARTBEAT_TIMER_STARTED");
    }

    private async Task SendImmediateHeartbeatAsync()
    {
        try
        {
            await SendHeartbeatAsync(includeMetrics: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            App.Log($"BOOTH_HEARTBEAT_IMMEDIATE_ERROR error={ex.GetType().Name}");
        }
    }

    public void StopHeartbeatTimer()
    {
        if (_heartbeatTimer == null)
        {
            return;
        }

        _heartbeatTimer.Dispose();
        _heartbeatTimer = null;
        App.Log("BOOTH_HEARTBEAT_TIMER_STOPPED");
    }

    private async void OnHeartbeatTimerTick(object? state)
    {
        try
        {
            var (ok, error) = await SendHeartbeatAsync(includeMetrics: true, CancellationToken.None);
            
            // If heartbeat fails, try ping endpoint as fallback
            if (!ok && (error == "timeout" || error?.StartsWith("http_error") == true))
            {
                App.Log($"BOOTH_HEARTBEAT_FAILED trying_ping_fallback error={error}");
                await SendPingAsync(CancellationToken.None);
            }
            
            // If still failing and we're not registered, try to re-register
            if (!ok && error == "booth_not_registered")
            {
                App.Log("BOOTH_NOT_REGISTERED attempting_re_registration");
                var (registerOk, registerError) = await RegisterAsync(cancellationToken: CancellationToken.None);
                if (registerOk)
                {
                    App.Log("BOOTH_RE_REGISTERED successfully");
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"BOOTH_HEARTBEAT_TIMER_ERROR error={ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Send a lightweight ping to the server (fallback if heartbeat fails)
    /// </summary>
    public async Task<(bool ok, string? error)> SendPingAsync(
        CancellationToken cancellationToken = default)
    {
        if (_settings.TestMode)
        {
            return (true, null);
        }

        if (!_isRegistered)
        {
            return (false, "not_registered");
        }

        var baseUrl = ResolveBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return (false, "missing_base_url");
        }

        var boothId = NormalizeBoothId(_settings.BoothId);
        if (string.IsNullOrWhiteSpace(boothId))
        {
            return (false, "missing_booth_id");
        }

        var payload = new
        {
            boothId
        };

        try
        {
            var url = $"{baseUrl}/api/booth/ping";
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                App.Log($"BOOTH_PING_FAILED status={(int)response.StatusCode}");
                return (false, $"http_error_{(int)response.StatusCode}");
            }

            _lastHeartbeatTime = DateTime.UtcNow;
            App.Log("BOOTH_PING_OK");
            return (true, null);
        }
        catch (TaskCanceledException)
        {
            App.Log("BOOTH_PING_FAILED reason=timeout");
            return (false, "timeout");
        }
        catch (Exception ex)
        {
            App.Log($"BOOTH_PING_FAILED error={ex.GetType().Name}");
            return (false, ex.GetType().Name);
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

    private static string GetSoftwareVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                return version.ToString();
            }
        }
        catch
        {
            // Ignore errors getting version
        }

        return "1.0.0";
    }

    private void InitializeMetrics()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
            _metricsInitialized = true;
        }
        catch
        {
            _metricsInitialized = false;
        }
    }

    private object? CollectDeviceMetrics()
    {
        if (!_metricsInitialized)
        {
            return null;
        }

        try
        {
            double? cpuUsage = null;
            double? memoryUsage = null;
            double? diskUsage = null;

            if (_cpuCounter != null)
            {
                cpuUsage = Math.Round(_cpuCounter.NextValue(), 1);
            }

            try
            {
                var process = Process.GetCurrentProcess();
                var workingSet = process.WorkingSet64;
                long totalPhysicalMemory = 0;
                
                try
                {
                    var memCounter = new PerformanceCounter("Memory", "Available Bytes");
                    var availableBytes = (long)memCounter.NextValue();
                    memCounter.Dispose();
                    
                    var totalCounter = new PerformanceCounter("Memory", "Committed Bytes");
                    var committedBytes = (long)totalCounter.NextValue();
                    totalCounter.Dispose();
                    
                    if (availableBytes > 0 && committedBytes > 0)
                    {
                        totalPhysicalMemory = availableBytes + committedBytes;
                    }
                }
                catch
                {
                    // Fallback: estimate based on common system memory sizes
                    totalPhysicalMemory = 8L * 1024 * 1024 * 1024;
                }
                
                if (totalPhysicalMemory > 0 && workingSet > 0)
                {
                    memoryUsage = Math.Round((double)workingSet / totalPhysicalMemory * 100, 1);
                }
            }
            catch
            {
                // Ignore memory usage errors
            }

            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                if (drive.IsReady && drive.TotalSize > 0)
                {
                    var freeSpace = drive.AvailableFreeSpace;
                    var totalSpace = drive.TotalSize;
                    diskUsage = Math.Round((double)(totalSpace - freeSpace) / totalSpace * 100, 1);
                }
            }
            catch
            {
                // Ignore disk usage errors
            }

            if (cpuUsage == null && memoryUsage == null && diskUsage == null)
            {
                return null;
            }

            return new
            {
                cpuUsage,
                memoryUsage,
                diskUsage
            };
        }
        catch
        {
            return null;
        }
    }
}
