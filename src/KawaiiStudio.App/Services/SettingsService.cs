using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class SettingsService
{
    private const int DefaultTimeoutSeconds = 45;

    private readonly string _settingsPath;
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public SettingsService(AppPaths appPaths)
    {
        _settingsPath = Path.Combine(appPaths.ConfigRoot, "appconfig.ini");
        LoadOrCreate();
    }

    public int MaxQuantity => GetInt("MAX_QUANTITY", 10);
    public decimal TokenValue => GetDecimal("TOKEN_VALUE", 1m);
    public string PrintName => GetString("PrintName", "DS-RX1");
    public string CashCom => GetString("cash_COM", "COM4");
    public int DefaultTimeout => GetInt("TIMEOUT_DEFAULT", DefaultTimeoutSeconds);
    public bool TestMode => GetBool("TEST_MODE", false);

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
        var lines = BuildOutputLines();
        File.WriteAllLines(_settingsPath, lines);
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
        foreach (var line in File.ReadAllLines(_settingsPath))
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
        _values["TOKEN_VALUE"] = "1";
        _values["PrintName"] = "DS-RX1";
        _values["cash_COM"] = "COM4";
        _values["CAMERA_PROVIDER"] = "simulated";
        _values["TEST_MODE"] = "false";
        _values["TIMEOUT_DEFAULT"] = DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _values["TIMEOUT_STARTUP"] = "45";
        _values["TIMEOUT_HOME"] = "45";
        _values["TIMEOUT_SIZE"] = "45";
        _values["TIMEOUT_QUANTITY"] = "45";
        _values["TIMEOUT_LAYOUT"] = "45";
        _values["TIMEOUT_CATEGORY"] = "45";
        _values["TIMEOUT_FRAME"] = "45";
        _values["TIMEOUT_PAYMENT"] = "45";
        _values["TIMEOUT_CAPTURE"] = "45";
        _values["TIMEOUT_REVIEW"] = "45";
        _values["TIMEOUT_FINALIZE"] = "45";
        _values["TIMEOUT_PRINTING"] = "45";
        _values["TIMEOUT_THANK_YOU"] = "45";
        _values["TIMEOUT_LIBRARY"] = "45";
        _values["TIMEOUT_STAFF"] = "45";
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
            ["TOKEN_VALUE"] = "1",
            ["PrintName"] = "DS-RX1",
            ["cash_COM"] = "COM4",
            ["CAMERA_PROVIDER"] = "simulated",
            ["TEST_MODE"] = "false",
            ["TIMEOUT_DEFAULT"] = DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            ["TIMEOUT_STARTUP"] = "45",
            ["TIMEOUT_HOME"] = "45",
            ["TIMEOUT_SIZE"] = "45",
            ["TIMEOUT_QUANTITY"] = "45",
            ["TIMEOUT_LAYOUT"] = "45",
            ["TIMEOUT_CATEGORY"] = "45",
            ["TIMEOUT_FRAME"] = "45",
            ["TIMEOUT_PAYMENT"] = "45",
            ["TIMEOUT_CAPTURE"] = "45",
            ["TIMEOUT_REVIEW"] = "45",
            ["TIMEOUT_FINALIZE"] = "45",
            ["TIMEOUT_PRINTING"] = "45",
            ["TIMEOUT_THANK_YOU"] = "45",
            ["TIMEOUT_LIBRARY"] = "45",
            ["TIMEOUT_STAFF"] = "45"
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
        var orderedKeys = new[]
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
            "PRICE5_46",
            "MAX_QUANTITY",
            "TOKEN_VALUE",
            "PrintName",
            "cash_COM",
            "CAMERA_PROVIDER",
            "TEST_MODE",
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
            "TIMEOUT_STAFF"
        };

        var lines = new List<string>();
        foreach (var key in orderedKeys)
        {
            if (_values.TryGetValue(key, out var value))
            {
                lines.Add($"{key}={value}");
            }
        }

        var remaining = _values.Keys
            .Except(orderedKeys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase);

        foreach (var key in remaining)
        {
            lines.Add($"{key}={_values[key]}");
        }

        return lines;
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
