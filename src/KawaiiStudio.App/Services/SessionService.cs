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

    public SessionState Current { get; } = new();

    public void StartNewSession()
    {
        var now = DateTime.Now;
        var sessionId = Guid.NewGuid().ToString("N");
        Current.Reset(sessionId, now, _runDateFolder);
        AppendLog($"SESSION_START id={sessionId}");
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
        File.AppendAllText(_logFilePath, $"{timestamp} {message}{Environment.NewLine}");
    }
}
