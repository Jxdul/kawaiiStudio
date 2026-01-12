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
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(session.SessionId ?? string.Empty), "sessionId");

            await using var imageStream = File.OpenRead(imagePath);
            var imageContent = new StreamContent(imageStream);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(GuessContentType(imagePath));
            form.Add(imageContent, "image", Path.GetFileName(imagePath));

            await using var videoStream = File.OpenRead(videoPath);
            var videoContent = new StreamContent(videoStream);
            videoContent.Headers.ContentType = new MediaTypeHeaderValue(GuessContentType(videoPath));
            form.Add(videoContent, "video", Path.GetFileName(videoPath));

            using var response = await _httpClient.PostAsync($"{baseUrl}/api/upload", form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, null, $"upload_failed_{(int)response.StatusCode}");
            }

            var url = TryReadUrl(body);
            if (string.IsNullOrWhiteSpace(url))
            {
                return (false, null, "missing_url");
            }

            return (true, url, null);
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
}
