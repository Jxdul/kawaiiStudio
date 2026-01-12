using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class VideoCompilationService
{
    private const int PreviewFrameRate = 6;
    private const int ProcessTimeoutSeconds = 60;
    private readonly string? _ffmpegPath;

    public VideoCompilationService(AppPaths appPaths)
    {
        _ffmpegPath = ResolveFfmpegPath(appPaths.AppRoot);
    }

    public bool TryBuildPreviewVideo(SessionState session, out string? outputPath, out string? error)
    {
        outputPath = null;
        error = null;

        var previewFolder = session.PreviewFramesFolder;
        var previewFrames = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(previewFolder) && Directory.Exists(previewFolder))
        {
            previewFrames = Directory.GetFiles(previewFolder)
                .Where(HasImageExtension)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var capturedFrames = session.CapturedPhotos
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .ToArray();

        var frames = ChooseFrames(previewFrames, capturedFrames);
        if (frames.Length == 0)
        {
            error = "video_frames_empty";
            return false;
        }

        var videosFolder = session.VideosFolder ?? session.SessionFolder;
        if (string.IsNullOrWhiteSpace(videosFolder))
        {
            error = "videos_folder_missing";
            return false;
        }

        Directory.CreateDirectory(videosFolder);
        var sessionId = string.IsNullOrWhiteSpace(session.SessionId) ? "session" : session.SessionId;
        outputPath = Path.Combine(videosFolder, $"{sessionId}_video.mp4");

        var ffmpeg = _ffmpegPath;
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            error = "ffmpeg_missing";
            return false;
        }

        var listFolder = !string.IsNullOrWhiteSpace(previewFolder) && Directory.Exists(previewFolder)
            ? previewFolder
            : videosFolder;
        var listPath = Path.Combine(listFolder, "preview_list.txt");
        WriteConcatFile(listPath, frames, PreviewFrameRate);
        var args = $"-nostdin -y -f concat -safe 0 -i \"{listPath}\" -vsync vfr -c:v libx264 -pix_fmt yuv420p \"{outputPath}\"";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args,
                WorkingDirectory = listFolder,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = false
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                error = "ffmpeg_start_failed";
                return false;
            }

            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ProcessTimeoutSeconds * 1000))
            {
                TryTerminate(process);
                error = "ffmpeg_timeout";
                return false;
            }

            if (process.ExitCode != 0)
            {
                var stderr = errorTask.GetAwaiter().GetResult();
                error = string.IsNullOrWhiteSpace(stderr) ? "ffmpeg_failed" : TruncateTail(stderr, 240);
                return false;
            }

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            error = Truncate(ex.Message, 240);
            return false;
        }
        finally
        {
            TryDelete(listPath);
        }
    }

    private static string? ResolveFfmpegPath(string appRoot)
    {
        if (string.IsNullOrWhiteSpace(appRoot))
        {
            return null;
        }

        var thirdParty = Path.Combine(appRoot, "third_party");
        if (!Directory.Exists(thirdParty))
        {
            return null;
        }

        var candidates = Directory.GetDirectories(thirdParty, "ffmpeg*")
            .Select(dir => Path.Combine(dir, "bin", "ffmpeg.exe"))
            .Where(File.Exists)
            .ToList();

        if (candidates.Count > 0)
        {
            return candidates[0];
        }

        var pathMatch = FindFfmpegOnPath();
        if (!string.IsNullOrWhiteSpace(pathMatch))
        {
            return pathMatch;
        }

        return "ffmpeg.exe";
    }

    private static bool HasImageExtension(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteConcatFile(string listPath, string[] frames, int frameRate)
    {
        var duration = frameRate > 0 ? (1.0 / frameRate) : 0.1;
        var durationText = duration.ToString("0.######", CultureInfo.InvariantCulture);
        var lines = new List<string>(frames.Length * 2 + 1);
        foreach (var frame in frames)
        {
            var normalized = frame.Replace('\\', '/');
            lines.Add($"file '{normalized}'");
            lines.Add($"duration {durationText}");
        }

        if (frames.Length > 0)
        {
            var last = frames[^1].Replace('\\', '/');
            lines.Add($"file '{last}'");
        }

        File.WriteAllLines(listPath, lines);
    }

    private static string? FindFfmpegOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown_error";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string TruncateTail(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown_error";
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[^maxLength..];
    }

    private static string[] ChooseFrames(string[] previewFrames, string[] capturedFrames)
    {
        if (previewFrames.Length > 0 && previewFrames.Length >= capturedFrames.Length)
        {
            return previewFrames;
        }

        if (capturedFrames.Length > 0)
        {
            return capturedFrames;
        }

        return Array.Empty<string>();
    }
}
