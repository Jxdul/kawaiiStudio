using System;
using System.IO;
using KawaiiStudio.App.Models;

namespace KawaiiStudio.App.Services;

public sealed class SessionService
{
    private readonly string _sessionsRoot;
    private readonly string _runDateFolder;
    private readonly string _logFilePath;

    public SessionService(AppPaths appPaths)
    {
        _sessionsRoot = appPaths.SessionsRoot;
        Directory.CreateDirectory(_sessionsRoot);
        _runDateFolder = Path.Combine(_sessionsRoot, DateTime.Today.ToString("yyyyMMdd"));
        Directory.CreateDirectory(_runDateFolder);
        _logFilePath = Path.Combine(_runDateFolder, "session.log");
    }

    public void PruneOldLogs(int retentionDays)
    {
        if (retentionDays <= 0 || !Directory.Exists(_sessionsRoot))
        {
            return;
        }

        var cutoff = DateTime.Today.AddDays(-retentionDays);
        foreach (var directory in Directory.GetDirectories(_sessionsRoot))
        {
            var name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!DateTime.TryParseExact(name, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date))
            {
                continue;
            }

            if (date < cutoff)
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch
                {
                    // Ignore cleanup failures to avoid blocking startup.
                }
            }
        }
    }

    public SessionState Current { get; } = new();

    public void StartNewSession()
    {
        var now = DateTime.Now;
        var sessionIndex = GetNextSessionIndex();
        var sessionName = $"session_{sessionIndex}";
        var sessionFolder = Path.Combine(_runDateFolder, sessionName);
        Directory.CreateDirectory(sessionFolder);

        var photosFolder = Path.Combine(sessionFolder, "photos");
        var previewFramesFolder = Path.Combine(sessionFolder, "preview_frames");
        var videosFolder = Path.Combine(sessionFolder, "videos");

        Directory.CreateDirectory(photosFolder);
        Directory.CreateDirectory(previewFramesFolder);
        Directory.CreateDirectory(videosFolder);

        Current.Reset(sessionName, now, sessionFolder, photosFolder, previewFramesFolder, videosFolder);
        AppendLog($"SESSION_START id={sessionName}");
    }

    public void EndSession()
    {
        Current.MarkCompleted(DateTime.Now);
        AppendLog($"SESSION_END id={Current.SessionId}");
    }

    public void RegisterCapturedPhoto(string filePath)
    {
        Current.AddCapturedPhoto(filePath);
    }

    public void SetFinalImagePath(string path)
    {
        Current.SetFinalImagePath(path);
    }

    public void SetVideoPath(string path)
    {
        Current.SetVideoPath(path);
    }

    public void SetQrUrl(string url)
    {
        Current.SetQrUrl(url);
    }

    public void SetPrintJob(string jobId, string? status = null)
    {
        Current.SetPrintJob(jobId, status);
    }

    public void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var line = $"{timestamp} {message}{Environment.NewLine}";

        try
        {
            using var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            writer.Write(line);
        }
        catch
        {
            // Avoid crashing the app if the log file is temporarily locked.
        }
    }

    private int GetNextSessionIndex()
    {
        var nextIndex = 1;
        if (!Directory.Exists(_runDateFolder))
        {
            return nextIndex;
        }

        foreach (var directory in Directory.GetDirectories(_runDateFolder, "session_*"))
        {
            var name = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var suffix = name.Substring("session_".Length);
            if (!int.TryParse(suffix, out var parsed))
            {
                continue;
            }

            if (parsed >= nextIndex)
            {
                nextIndex = parsed + 1;
            }
        }

        return nextIndex;
    }
}
