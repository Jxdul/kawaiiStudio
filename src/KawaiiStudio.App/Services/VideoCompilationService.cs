using System;
using System.Diagnostics;
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
        if (string.IsNullOrWhiteSpace(previewFolder) || !Directory.Exists(previewFolder))
        {
            error = "preview_frames_missing";
            return false;
        }

        var frames = Directory.GetFiles(previewFolder, "preview_*.png");
        if (frames.Length == 0)
        {
            error = "preview_frames_empty";
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

        var inputPattern = Path.Combine(previewFolder, "preview_%04d.png");
        var args = $"-nostdin -y -framerate {PreviewFrameRate} -start_number 1 -i \"{inputPattern}\" -c:v libx264 -pix_fmt yuv420p \"{outputPath}\"";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args,
                WorkingDirectory = previewFolder,
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
                error = string.IsNullOrWhiteSpace(stderr) ? "ffmpeg_failed" : Truncate(stderr, 240);
                return false;
            }

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            error = Truncate(ex.Message, 240);
            return false;
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

        return null;
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
}
