using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class SettingsService
{
    private const int DefaultTimeoutSeconds = 30;

    private readonly string _settingsPath;
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _rawLines = new();

    public SettingsService(AppPaths appPaths)
    {
        _settingsPath = Path.Combine(appPaths.ConfigRoot, "appconfig.ini");
        LoadOrCreate();
    }

    public int MaxQuantity => GetInt("MAX_QUANTITY", 10);
    public string PrintName => GetString("PrintName", "DS-RX1");
    public string TwoBySixPrintTicketPath => GetString("PRINT_TICKET_2X6", string.Empty);
    public string CashCom => GetString("cash_COM", "COM4");
    public int DefaultTimeout => GetInt("TIMEOUT_DEFAULT", DefaultTimeoutSeconds);
    public int CameraTimerSeconds => GetInt("CAMERA_TIMER_SECONDS", 3);
    public bool TestMode => GetBool("TEST_MODE", false);
    public IReadOnlyCollection<int> CashDenominations => GetCashDenominations();
    public bool CashLogAll => GetBool("CASH_LOG_ALL", false);
    public string CardProvider => GetString("CARD_PROVIDER", "simulated");
    public string StripeTerminalBaseUrl => GetString("STRIPE_TERMINAL_BASE_URL", "https://kawaii-studio-server.jxdul.workers.dev");
    public string StripeTerminalReaderId => GetString("STRIPE_TERMINAL_READER_ID", string.Empty);
    public string StripeTerminalLocationId => GetString("STRIPE_TERMINAL_LOCATION_ID", string.Empty);
    public string UploadBaseUrl => GetString("UPLOAD_BASE_URL", string.Empty);
    public bool UploadEnabled => GetBool("UPLOAD_ENABLED", false);
    public string BoothId => GetString("BOOTH_ID", string.Empty);

    public string GetValue(string key, string fallback = "")
    {
        return GetString(key, fallback);
    }

    public void SetValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _values[key.Trim()] = value?.Trim() ?? string.Empty;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? ".");
        var lines = _rawLines.Count > 0
            ? BuildOutputLinesPreservingComments()
            : BuildOutputLines();
        File.WriteAllLines(_settingsPath, lines);
        _rawLines = lines.ToList();
    }

    public void Reload()
    {
        LoadFromFile();
        EnsureDefaults();
        Save();
    }

    public decimal GetPrice(PrintSize? size, int? quantity)
    {
        var sizeCode = GetSizeCode(size);
        return GetPriceByCode(sizeCode, quantity);
    }

    public decimal GetPrice(string? templateType, int? quantity)
    {
        var sizeCode = GetSizeCode(templateType);
        return GetPriceByCode(sizeCode, quantity);
    }

    public int GetTimeoutSeconds(string? screenKey)
    {
        var key = GetTimeoutKey(screenKey);
        return key is null ? DefaultTimeout : GetInt(key, DefaultTimeout);
    }

    private IReadOnlyCollection<int> GetCashDenominations()
    {
        var raw = GetString("CASH_DENOMS", "5,10,20");
        var parsed = ParseCashDenominations(raw);
        if (parsed.Count == 0)
        {
            parsed.Add(5);
            parsed.Add(10);
            parsed.Add(20);
        }

        return parsed;
    }

    private static HashSet<int> ParseCashDenominations(string? raw)
    {
        var results = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return results;
        }

        var parts = raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && value > 0)
            {
                results.Add(value);
            }
        }

        return results;
    }

    private decimal GetPriceByCode(string? sizeCode, int? quantity)
    {
        if (string.IsNullOrWhiteSpace(sizeCode) || quantity is null || quantity <= 0)
        {
            return 0m;
        }

        if (quantity.Value % 2 != 0)
        {
            return 0m;
        }

        var pairCount = quantity.Value / 2;
        if (pairCount <= 0)
        {
            return 0m;
        }

        var key = $"PRICE{pairCount}_{sizeCode}";
        return GetDecimal(key, 0m);
    }

    private static string? GetSizeCode(PrintSize? size)
    {
        return size switch
        {
            PrintSize.TwoBySix => "26",
            PrintSize.FourBySix => "46",
            _ => null
        };
    }

    private static string? GetSizeCode(string? templateType)
    {
        if (string.IsNullOrWhiteSpace(templateType))
        {
            return null;
        }

        if (templateType.StartsWith("2x6", StringComparison.OrdinalIgnoreCase))
        {
            return "26";
        }

        if (templateType.StartsWith("4x6", StringComparison.OrdinalIgnoreCase))
        {
            return "46";
        }

        return null;
    }

    private static string? GetTimeoutKey(string? screenKey)
    {
        if (string.IsNullOrWhiteSpace(screenKey))
        {
            return null;
        }

        return screenKey.ToLowerInvariant() switch
        {
            "startup" => "TIMEOUT_STARTUP",
            "home" => "TIMEOUT_HOME",
            "size" => "TIMEOUT_SIZE",
            "quantity" => "TIMEOUT_QUANTITY",
            "layout" => "TIMEOUT_LAYOUT",
            "category" => "TIMEOUT_CATEGORY",
            "frame" => "TIMEOUT_FRAME",
            "payment" => "TIMEOUT_PAYMENT",
            "capture" => "TIMEOUT_CAPTURE",
            "review" => "TIMEOUT_REVIEW",
            "finalize" => "TIMEOUT_FINALIZE",
            "printing" => "TIMEOUT_PRINTING",
            "thank_you" => "TIMEOUT_THANK_YOU",
            "library" => "TIMEOUT_LIBRARY",
            "staff" => "TIMEOUT_STAFF",
            _ => null
        };
    }

    private void LoadOrCreate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath) ?? ".");

        if (!File.Exists(_settingsPath))
        {
            LoadDefaults();
            Save();
            return;
        }

        LoadFromFile();
        EnsureDefaults();
        Save();
    }

    private void LoadFromFile()
    {
        _values.Clear();
        var lines = File.ReadAllLines(_settingsPath);
        _rawLines = new List<string>(lines);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            _values[key] = value;
        }
    }

    private void LoadDefaults()
    {
        _values.Clear();
        _rawLines = new List<string>();
        _values["PRICE1_26"] = "10";
        _values["PRICE2_26"] = "20";
        _values["PRICE3_26"] = "30";
        _values["PRICE4_26"] = "40";
        _values["PRICE5_26"] = "50";
        _values["PRICE1_46"] = "15";
        _values["PRICE2_46"] = "30";
        _values["PRICE3_46"] = "45";
        _values["PRICE4_46"] = "60";
        _values["PRICE5_46"] = "75";
        _values["MAX_QUANTITY"] = "10";
        _values["CASH_DENOMS"] = "5,10,20";
        _values["CASH_LOG_ALL"] = "false";
        _values["PrintName"] = "DS-RX1";
        _values["PRINT_TICKET_2X6"] = string.Empty;
        _values["cash_COM"] = "COM4";
        _values["CARD_PROVIDER"] = "simulated";
        _values["STRIPE_TERMINAL_BASE_URL"] = "https://kawaii-studio-server.jxdul.workers.dev";
        _values["STRIPE_TERMINAL_READER_ID"] = string.Empty;
        _values["STRIPE_TERMINAL_LOCATION_ID"] = string.Empty;
        _values["UPLOAD_BASE_URL"] = string.Empty;
        _values["UPLOAD_ENABLED"] = "false";
        _values["BOOTH_ID"] = string.Empty;
        _values["CAMERA_PROVIDER"] = "simulated";
        _values["TEST_MODE"] = "false";
        _values["TIMEOUT_DEFAULT"] = DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _values["TIMEOUT_STARTUP"] = "30";
        _values["TIMEOUT_HOME"] = "30";
        _values["TIMEOUT_SIZE"] = "30";
        _values["TIMEOUT_QUANTITY"] = "30";
        _values["TIMEOUT_LAYOUT"] = "30";
        _values["TIMEOUT_CATEGORY"] = "30";
        _values["TIMEOUT_FRAME"] = "30";
        _values["TIMEOUT_PAYMENT"] = "30";
        _values["TIMEOUT_CAPTURE"] = "30";
        _values["TIMEOUT_REVIEW"] = "30";
        _values["TIMEOUT_FINALIZE"] = "30";
        _values["TIMEOUT_PRINTING"] = "30";
        _values["TIMEOUT_THANK_YOU"] = "30";
        _values["TIMEOUT_LIBRARY"] = "30";
        _values["TIMEOUT_STAFF"] = "30";
        _values["CAMERA_TIMER_SECONDS"] = "3";
    }

    private void EnsureDefaults()
    {
        if (_values.Count == 0)
        {
            LoadDefaults();
            return;
        }

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PRICE1_26"] = "10",
            ["PRICE2_26"] = "20",
            ["PRICE3_26"] = "30",
            ["PRICE4_26"] = "40",
            ["PRICE5_26"] = "50",
            ["PRICE1_46"] = "15",
            ["PRICE2_46"] = "30",
            ["PRICE3_46"] = "45",
            ["PRICE4_46"] = "60",
            ["PRICE5_46"] = "75",
            ["MAX_QUANTITY"] = "10",
            ["CASH_DENOMS"] = "5,10,20",
            ["CASH_LOG_ALL"] = "false",
            ["PrintName"] = "DS-RX1",
            ["PRINT_TICKET_2X6"] = string.Empty,
            ["cash_COM"] = "COM4",
            ["CARD_PROVIDER"] = "simulated",
            ["STRIPE_TERMINAL_BASE_URL"] = "https://kawaii-studio-server.jxdul.workers.dev",
            ["STRIPE_TERMINAL_READER_ID"] = string.Empty,
            ["STRIPE_TERMINAL_LOCATION_ID"] = string.Empty,
            ["UPLOAD_BASE_URL"] = string.Empty,
            ["UPLOAD_ENABLED"] = "false",
            ["BOOTH_ID"] = string.Empty,
            ["CAMERA_PROVIDER"] = "simulated",
            ["TEST_MODE"] = "false",
            ["TIMEOUT_DEFAULT"] = DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["TIMEOUT_STARTUP"] = "30",
            ["TIMEOUT_HOME"] = "30",
            ["TIMEOUT_SIZE"] = "30",
            ["TIMEOUT_QUANTITY"] = "30",
            ["TIMEOUT_LAYOUT"] = "30",
            ["TIMEOUT_CATEGORY"] = "30",
            ["TIMEOUT_FRAME"] = "30",
            ["TIMEOUT_PAYMENT"] = "30",
            ["TIMEOUT_CAPTURE"] = "30",
            ["TIMEOUT_REVIEW"] = "30",
            ["TIMEOUT_FINALIZE"] = "30",
            ["TIMEOUT_PRINTING"] = "30",
            ["TIMEOUT_THANK_YOU"] = "30",
            ["TIMEOUT_LIBRARY"] = "30",
            ["TIMEOUT_STAFF"] = "30",
            ["CAMERA_TIMER_SECONDS"] = "3"
        };

        foreach (var pair in defaults)
        {
            if (!_values.ContainsKey(pair.Key))
            {
                _values[pair.Key] = pair.Value;
            }
        }
    }

    private IEnumerable<string> BuildOutputLines()
    {
        var groups = new[]
        {
            new[]
            {
                "PRICE1_26",
                "PRICE2_26",
                "PRICE3_26",
                "PRICE4_26",
                "PRICE5_26",
                "PRICE1_46",
                "PRICE2_46",
                "PRICE3_46",
                "PRICE4_46",
                "PRICE5_46"
            },
            new[]
            {
                "MAX_QUANTITY",
                "CASH_DENOMS",
                "CASH_LOG_ALL"
            },
            new[]
            {
                "PrintName",
                "PRINT_TICKET_2X6",
                "cash_COM",
                "CARD_PROVIDER",
                "STRIPE_TERMINAL_BASE_URL",
                "STRIPE_TERMINAL_READER_ID",
                "STRIPE_TERMINAL_LOCATION_ID",
                "UPLOAD_BASE_URL",
                "UPLOAD_ENABLED",
                "BOOTH_ID",
                "CAMERA_PROVIDER",
                "TEST_MODE"
            },
            new[]
            {
                "TIMEOUT_DEFAULT",
                "TIMEOUT_STARTUP",
                "TIMEOUT_HOME",
                "TIMEOUT_SIZE",
                "TIMEOUT_QUANTITY",
                "TIMEOUT_LAYOUT",
                "TIMEOUT_CATEGORY",
                "TIMEOUT_FRAME",
                "TIMEOUT_PAYMENT",
                "TIMEOUT_CAPTURE",
                "TIMEOUT_REVIEW",
                "TIMEOUT_FINALIZE",
                "TIMEOUT_PRINTING",
                "TIMEOUT_THANK_YOU",
                "TIMEOUT_LIBRARY",
                "TIMEOUT_STAFF",
                "CAMERA_TIMER_SECONDS"
            }
        };

        var groupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var key in group)
            {
                groupKeys.Add(key);
            }
        }

        var lines = new List<string>();
        foreach (var group in groups)
        {
            var groupLines = new List<string>();
            foreach (var key in group)
            {
                if (_values.TryGetValue(key, out var value))
                {
                    groupLines.Add($"{key}={value}");
                }
            }

            if (groupLines.Count == 0)
            {
                continue;
            }

            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.AddRange(groupLines);
        }

        var remaining = _values.Keys
            .Except(groupKeys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (remaining.Count > 0)
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            foreach (var key in remaining)
            {
                lines.Add($"{key}={_values[key]}");
            }
        }

        return lines;
    }

    private IEnumerable<string> BuildOutputLinesPreservingComments()
    {
        var output = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in _rawLines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                output.Add(line);
                continue;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal)
                || trimmed.StartsWith(";", StringComparison.Ordinal))
            {
                output.Add(line);
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                output.Add(line);
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                output.Add(line);
                continue;
            }

            if (_values.TryGetValue(key, out var value))
            {
                output.Add($"{key}={value}");
                seenKeys.Add(key);
            }
            else
            {
                output.Add(line);
            }
        }

        var missing = _values.Keys
            .Where(key => !seenKeys.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count > 0)
        {
            if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[^1]))
            {
                output.Add(string.Empty);
            }

            foreach (var key in missing)
            {
                output.Add($"{key}={_values[key]}");
            }
        }

        return output;
    }

    private string GetString(string key, string fallback)
    {
        return _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private int GetInt(string key, int fallback)
    {
        var raw = GetString(key, string.Empty);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private bool GetBool(string key, bool fallback)
    {
        var raw = GetString(key, string.Empty);
        return bool.TryParse(raw, out var value) ? value : fallback;
    }

    private decimal GetDecimal(string key, decimal fallback)
    {
        var raw = GetString(key, string.Empty);
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }
}
