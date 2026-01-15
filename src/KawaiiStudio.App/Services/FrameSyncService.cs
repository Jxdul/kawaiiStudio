using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KawaiiStudio.App.Services;

public sealed class FrameSyncService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly SettingsService _settings;
    private readonly string _framesRoot;
    private readonly FrameCatalogService _frameCatalog;
    private readonly HttpClient _httpClient;

    public FrameSyncService(SettingsService settings, string framesRoot, FrameCatalogService frameCatalog)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _framesRoot = framesRoot ?? throw new ArgumentNullException(nameof(framesRoot));
        _frameCatalog = frameCatalog ?? throw new ArgumentNullException(nameof(frameCatalog));
        _httpClient = new HttpClient
        {
            Timeout = DefaultTimeout
        };
    }

    public async Task<(bool ok, string? error)> SyncFramesAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            App.Log("FRAME_SYNC_SKIPPED reason=missing_base_url");
            return (false, "missing_base_url");
        }

        try
        {
            App.Log("FRAME_SYNC_START");
            
            // Fetch server frames
            var serverFramesResult = await GetServerFramesAsync(baseUrl, cancellationToken);
            if (!serverFramesResult.ok || serverFramesResult.frames == null)
            {
                App.Log($"FRAME_SYNC_FAILED reason={serverFramesResult.error ?? "unknown"}");
                return (false, serverFramesResult.error ?? "unknown");
            }

            var serverFrames = serverFramesResult.frames;
            App.Log($"FRAME_SYNC_SERVER_COUNT count={serverFrames.Count}");

            // Get local frames
            var localFrames = GetLocalFrames();
            App.Log($"FRAME_SYNC_LOCAL_COUNT count={localFrames.Count}");

            // Build maps for comparison
            var serverFrameMap = new Dictionary<string, ServerFrameInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var frame in serverFrames)
            {
                serverFrameMap[frame.key] = frame;
            }

            var localFrameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, path) in localFrames)
            {
                localFrameMap[key] = path;
            }

            // Delete local frames not on server
            var deletedCount = 0;
            foreach (var (key, localPath) in localFrameMap)
            {
                if (!serverFrameMap.ContainsKey(key))
                {
                    try
                    {
                        if (File.Exists(localPath))
                        {
                            File.Delete(localPath);
                            deletedCount++;
                            App.Log($"FRAME_SYNC_DELETE key={key}");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Log($"FRAME_SYNC_DELETE_FAILED key={key} error={ex.GetType().Name}");
                    }
                }
            }

            // Download new/missing frames from server
            var downloadedCount = 0;
            foreach (var (key, serverFrame) in serverFrameMap)
            {
                if (!localFrameMap.ContainsKey(key))
                {
                    var downloadResult = await DownloadFrameAsync(baseUrl, key, cancellationToken);
                    if (downloadResult.ok)
                    {
                        downloadedCount++;
                        App.Log($"FRAME_SYNC_DOWNLOAD key={key}");
                    }
                    else
                    {
                        App.Log($"FRAME_SYNC_DOWNLOAD_FAILED key={key} error={downloadResult.error}");
                    }
                }
            }

            // Clear cache so frames are reloaded
            _frameCatalog.ClearCache();
            
            App.Log($"FRAME_SYNC_COMPLETE deleted={deletedCount} downloaded={downloadedCount}");
            return (true, null);
        }
        catch (TaskCanceledException)
        {
            App.Log("FRAME_SYNC_FAILED reason=timeout");
            return (false, "timeout");
        }
        catch (Exception ex)
        {
            App.Log($"FRAME_SYNC_FAILED error={ex.GetType().Name}");
            return (false, ex.GetType().Name);
        }
    }

    private string? ResolveBaseUrl()
    {
        return _settings.StripeTerminalBaseUrl?.Trim().TrimEnd('/');
    }

    private async Task<(bool ok, List<ServerFrameInfo>? frames, string? error)> GetServerFramesAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{baseUrl}/api/frames/sync";
            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, null, $"http_error_{(int)response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);

            if (!json.RootElement.TryGetProperty("ok", out var okValue) || !okValue.GetBoolean())
            {
                return (false, null, "server_error");
            }

            if (!json.RootElement.TryGetProperty("frames", out var framesArray))
            {
                return (false, null, "invalid_response");
            }

            var frames = new List<ServerFrameInfo>();
            foreach (var frameElement in framesArray.EnumerateArray())
            {
                if (frameElement.TryGetProperty("key", out var keyElement) &&
                    frameElement.TryGetProperty("size", out var sizeElement))
                {
                    frames.Add(new ServerFrameInfo
                    {
                        key = keyElement.GetString() ?? string.Empty,
                        size = sizeElement.GetInt64(),
                        uploaded = frameElement.TryGetProperty("uploaded", out var uploadedElement)
                            ? uploadedElement.GetString()
                            : null
                    });
                }
            }

            return (true, frames, null);
        }
        catch (TaskCanceledException)
        {
            return (false, null, "timeout");
        }
        catch (Exception ex)
        {
            return (false, null, ex.GetType().Name);
        }
    }

    private Dictionary<string, string> GetLocalFrames()
    {
        var frames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_framesRoot))
        {
            Directory.CreateDirectory(_framesRoot);
            return frames;
        }

        // Scan 2x6 frames
        var twoBySixRoot = Path.Combine(_framesRoot, "2x6");
        if (Directory.Exists(twoBySixRoot))
        {
            foreach (var categoryDir in Directory.EnumerateDirectories(twoBySixRoot))
            {
                var categoryName = Path.GetFileName(categoryDir);
                foreach (var frameFile in Directory.EnumerateFiles(categoryDir, "*.png"))
                {
                    var fileName = Path.GetFileName(frameFile);
                    var key = $"Config/frames/2x6/{categoryName}/{fileName}";
                    frames[key] = frameFile;
                }
            }
        }

        // Scan 4x6 frames
        var fourBySixRoot = Path.Combine(_framesRoot, "4x6");
        if (Directory.Exists(fourBySixRoot))
        {
            foreach (var slotDir in Directory.EnumerateDirectories(fourBySixRoot))
            {
                var slotName = Path.GetFileName(slotDir);
                if (slotName != "2slots" && slotName != "4slots" && slotName != "6slots")
                {
                    continue;
                }

                foreach (var categoryDir in Directory.EnumerateDirectories(slotDir))
                {
                    var categoryName = Path.GetFileName(categoryDir);
                    foreach (var frameFile in Directory.EnumerateFiles(categoryDir, "*.png"))
                    {
                        var fileName = Path.GetFileName(frameFile);
                        var key = $"Config/frames/4x6/{slotName}/{categoryName}/{fileName}";
                        frames[key] = frameFile;
                    }
                }
            }
        }

        return frames;
    }

    private async Task<(bool ok, string? error)> DownloadFrameAsync(
        string baseUrl,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            // Convert R2 key to local path
            // Key format: Config/frames/2x6/Category/frame.png
            // Key format: Config/frames/4x6/2slots/Category/frame.png
            var localPath = ConvertKeyToLocalPath(key);
            if (string.IsNullOrWhiteSpace(localPath))
            {
                return (false, "invalid_key");
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(localPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return (false, "invalid_path");
            }

            Directory.CreateDirectory(directory);

            // Download frame from server
            var url = $"{baseUrl}/api/frames/download?key={Uri.EscapeDataString(key)}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"http_error_{(int)response.StatusCode}");
            }

            // Save to file
            await using var fileStream = File.Create(localPath);
            await response.Content.CopyToAsync(fileStream, cancellationToken);

            return (true, null);
        }
        catch (TaskCanceledException)
        {
            return (false, "timeout");
        }
        catch (Exception ex)
        {
            return (false, ex.GetType().Name);
        }
    }

    private string? ConvertKeyToLocalPath(string key)
    {
        // Key format: Config/frames/2x6/Category/frame.png
        // Key format: Config/frames/4x6/2slots/Category/frame.png
        // Local path should be: Config/frames/2x6/Category/frame.png (relative to framesRoot's parent)

        if (!key.StartsWith("Config/frames/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Remove "Config/frames/" prefix and combine with framesRoot
        var relativePath = key.Substring("Config/frames/".Length);
        var localPath = Path.Combine(_framesRoot, relativePath);

        // Normalize path separators for Windows
        return Path.GetFullPath(localPath);
    }

    private sealed class ServerFrameInfo
    {
        public string key { get; set; } = string.Empty;
        public long size { get; set; }
        public string? uploaded { get; set; }
    }
}