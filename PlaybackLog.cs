using System;
using System.IO;

namespace AimpSmtc;

/// <summary>
/// Structured playback event log. Written to
/// %LOCALAPPDATA%\AimpSmtc\smtc.log.
/// Separate from AppIdentity.Log (error.log) which is for crashes only.
/// </summary>
internal static class PlaybackLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AimpSmtc", "smtc.log");

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { /* logging must never crash the app */ }
    }

    public static string FormatDuration(long ms)
    {
        if (ms <= 0) return "unknown";
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }
}
