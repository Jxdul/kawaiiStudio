using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class UploadService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;

    public UploadService(SettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = new HttpClient
        {
            Timeout = DefaultTimeout
        };
    }

    public bool IsEnabled => _settings.UploadEnabled;

    public async Task<(bool ok, string? url, string? error)> UploadAsync(
        SessionState session,
        string imagePath,
        string videoPath,
        CancellationToken cancellationToken)
    {
        var baseUrl = _settings.GetValue("UPLOAD_BASE_URL", string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return (false, null, "missing_base_url");
        }

        if (!File.Exists(imagePath))
        {
            return (false, null, "image_missing");
        }

        if (!File.Exists(videoPath))
        {
            return (false, null, "video_missing");
        }

        try
        {
            var initResult = await PostJsonAsync(
                $"{baseUrl}/api/upload/init",
                new { sessionId = session.SessionId ?? string.Empty },
                cancellationToken);

            if (!initResult.ok)
            {
                return (false, null, $"upload_failed_{initResult.error}");
            }

            var token = initResult.token;
            var imageUrl = initResult.imageUrl;
            var videoUrl = initResult.videoUrl;
            var completeUrl = initResult.completeUrl ?? $"{baseUrl}/api/upload/complete";
            var shareUrl = initResult.shareUrl;

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(videoUrl))
            {
                return (false, null, "upload_failed_missing_init");
            }

            var imageOk = await PutFileAsync(imageUrl, imagePath, cancellationToken);
            if (!imageOk.ok)
            {
                return (false, null, $"upload_failed_{imageOk.error}");
            }

            var videoOk = await PutFileAsync(videoUrl, videoPath, cancellationToken);
            if (!videoOk.ok)
            {
                return (false, null, $"upload_failed_{videoOk.error}");
            }

            var completeResult = await PostJsonAsync(
                completeUrl,
                new { token, sessionId = session.SessionId ?? string.Empty },
                cancellationToken);

            if (!completeResult.ok)
            {
                return (false, null, $"upload_failed_{completeResult.error}");
            }

            var finalUrl = !string.IsNullOrWhiteSpace(completeResult.url)
                ? completeResult.url
                : shareUrl;

            if (string.IsNullOrWhiteSpace(finalUrl))
            {
                return (false, null, "missing_url");
            }

            return (true, finalUrl, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private static string GuessContentType(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            _ => "application/octet-stream"
        };
    }

    private static string? TryReadUrl(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("url", out var urlValue))
            {
                return urlValue.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? TryReadError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var errorValue))
            {
                return errorValue.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task<(bool ok, string? token, string? imageUrl, string? videoUrl, string? completeUrl, string? shareUrl, string? url, string? error)> PostJsonAsync(
        string url,
        object payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorDetail = TryReadError(body);
            var suffix = string.IsNullOrWhiteSpace(errorDetail)
                ? $"{(int)response.StatusCode}"
                : $"{(int)response.StatusCode}_{errorDetail}";
            return (false, null, null, null, null, null, null, suffix);
        }

        return (true,
            TryReadString(body, "token"),
            TryReadString(body, "imageUrl"),
            TryReadString(body, "videoUrl"),
            TryReadString(body, "completeUrl"),
            TryReadString(body, "shareUrl"),
            TryReadUrl(body),
            null);
    }

    private async Task<(bool ok, string? error)> PutFileAsync(string url, string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(GuessContentType(path));
        using var response = await _httpClient.PutAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorDetail = TryReadError(body);
            var suffix = string.IsNullOrWhiteSpace(errorDetail)
                ? $"{(int)response.StatusCode}"
                : $"{(int)response.StatusCode}_{errorDetail}";
            return (false, suffix);
        }

        return (true, null);
    }

    private static string? TryReadString(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(property, out var value))
            {
                return value.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
