using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace AimpSmtc;

// Models

public enum AimpStatus { Stopped = 0, Paused = 1, Playing = 2 }

public sealed class AimpInfo
{
    public string Title    { get; init; } = "";
    public string Artist   { get; init; } = "";
    public string Album    { get; init; } = "";
    public string FilePath { get; init; } = "";
    public long   Duration { get; init; } // ms
}

// AIMP Remote API
// Source: aimp_sdk/Sources/Cpp/apiRemote.h (v5.40 build 2650)
//
// TAIMPRemoteFileInfo struct (packed, starts at offset 0 of the
// memory-mapped file "AIMP2_RemoteInfo"):
//   [0x00] DWORD  Deprecated1
//   [0x04] BOOL   Active           - non-zero when AIMP is open with a track loaded
//   [0x08] DWORD  BitRate
//   [0x0C] DWORD  Channels
//   [0x10] DWORD  Duration         - ms
//   [0x14] INT64  FileSize         - bytes
//   [0x1C] DWORD  FileMark
//   [0x20] DWORD  SampleRate
//   [0x24] DWORD  TrackNumber
//   [0x28] DWORD  AlbumLength      - length in UTF-16 chars
//   [0x2C] DWORD  ArtistLength
//   [0x30] DWORD  DateLength
//   [0x34] DWORD  FileNameLength
//   [0x38] DWORD  GenreLength
//   [0x3C] DWORD  TitleLength
//   [0x40] DWORD  Deprecated2[6]   - 24 bytes
//   [0x58] UTF-16 strings, back to back: Album | Artist | Date | FileName | Genre | Title
//
// Window messages sent to FindWindow("AIMP2_RemoteInfo", null):
//   WM_AIMP_COMMAND  = WM_USER + 0x75  (fire-and-forget commands)
//   WM_AIMP_PROPERTY = WM_USER + 0x77  (get/set a property)
//
//   GET property: SendMessage(hwnd, WM_AIMP_PROPERTY, propId | 0 /*GET*/, 0)
//   SET property: SendMessage(hwnd, WM_AIMP_PROPERTY, propId | 1 /*SET*/, value)
//
// Properties:
//   AIMP_RA_PROPERTY_PLAYER_POSITION = 0x20  (ms, GET and SET - SET performs a seek)
//   AIMP_RA_PROPERTY_PLAYER_DURATION = 0x30  (ms, GET only)
//   AIMP_RA_PROPERTY_PLAYER_STATE    = 0x40  (0 = Stopped, 1 = Paused, 2 = Playing)
//   AIMP_RA_PROPERTY_TRACK_REPEAT    = 0x70  (0 or 1)
//   AIMP_RA_PROPERTY_TRACK_SHUFFLE   = 0x80  (0 or 1)
//
// Commands:
//   AIMP_RA_CMD_PLAY         = 13
//   AIMP_RA_CMD_PLAYPAUSE    = 14
//   AIMP_RA_CMD_PAUSE        = 15
//   AIMP_RA_CMD_STOP         = 16
//   AIMP_RA_CMD_NEXT         = 17
//   AIMP_RA_CMD_PREV         = 18
//   AIMP_RA_CMD_GET_ALBUMART = 29  (LParam = HWND that should receive the WM_COPYDATA reply)

public static class AimpRemote
{
    private const string MapName  = "AIMP2_RemoteInfo";
    private const string WndClass = "AIMP2_RemoteInfo";
    private const int    MapSize  = 2048;

    private const uint WM_USER          = 0x0400;
    private const uint WM_AIMP_COMMAND  = WM_USER + 0x75;
    private const uint WM_AIMP_PROPERTY = WM_USER + 0x77;

    private const int PROP_GET      = 0;
    private const int PROP_SET      = 1;
    private const int PROP_POSITION = 0x20;
    private const int PROP_DURATION = 0x30;
    private const int PROP_STATE    = 0x40;
    private const int PROP_REPEAT   = 0x70;
    private const int PROP_SHUFFLE  = 0x80;

    private const int CMD_PLAY         = 13;
    private const int CMD_PLAYPAUSE    = 14;
    private const int CMD_PAUSE        = 15;
    private const int CMD_STOP         = 16;
    private const int CMD_NEXT         = 17;
    private const int CMD_PREV         = 18;
    private const int CMD_GET_ALBUMART = 29;

    public const int WM_AIMP_COPYDATA_ALBUMART_ID = 0x41495043;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string cls, string? title);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc fn, IntPtr lp);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lp);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private static IntPtr RemoteWindow() => FindWindow(WndClass, null);
    private static IntPtr? _mainWnd;

    public static IntPtr GetAimpMainWindowHandle()
    {
        var remote = RemoteWindow();
        if (remote == IntPtr.Zero) { _mainWnd = null; return IntPtr.Zero; }
        if (_mainWnd.HasValue) return _mainWnd.Value;

        GetWindowThreadProcessId(remote, out uint pid);
        IntPtr main = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint p);
            if (p == pid && IsWindowVisible(hwnd) && GetWindowTextLength(hwnd) > 0)
            { main = hwnd; return false; }
            return true;
        }, IntPtr.Zero);

        _mainWnd = main != IntPtr.Zero ? main : remote;
        return _mainWnd.Value;
    }

    public static uint GetAimpProcessId()
    {
        var hwnd = RemoteWindow();
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    public static bool IsRunning() => RemoteWindow() != IntPtr.Zero;

    // Properties (WM_AIMP_PROPERTY)

    public static AimpStatus GetState()
    {
        var hwnd = RemoteWindow();
        if (hwnd == IntPtr.Zero) return AimpStatus.Stopped;
        int v = (int)SendMessage(hwnd, WM_AIMP_PROPERTY, new IntPtr(PROP_STATE | PROP_GET), IntPtr.Zero);
        return (AimpStatus)Math.Clamp(v, 0, 2);
    }

    public static long GetPosition()
    {
        var hwnd = RemoteWindow();
        if (hwnd == IntPtr.Zero) return 0;
        return (long)SendMessage(hwnd, WM_AIMP_PROPERTY, new IntPtr(PROP_POSITION | PROP_GET), IntPtr.Zero);
    }

    public static long GetDuration()
    {
        var hwnd = RemoteWindow();
        if (hwnd == IntPtr.Zero) return 0;
        return (long)SendMessage(hwnd, WM_AIMP_PROPERTY, new IntPtr(PROP_DURATION | PROP_GET), IntPtr.Zero);
    }

    public static void SetPosition(long ms)
    {
        var hwnd = RemoteWindow();
        if (hwnd == IntPtr.Zero) return;
        SendMessage(hwnd, WM_AIMP_PROPERTY, new IntPtr(PROP_POSITION | PROP_SET), new IntPtr(ms));
    }

    public static bool GetRepeat()
    {
        var hwnd = RemoteWindow();
        if (hwnd == IntPtr.Zero) return false;
        return SendMessage(hwnd, WM_AIMP_PROPERTY, new IntPtr(PROP_REPEAT | PROP_GET), IntPtr.Zero) != IntPtr.Zero;
    }

    public static void SetRepeat(bool enabled)
    {
        var hwnd = RemoteWindow();
        if (hwnd == IntPtr.Zero) return;
        SendMessage(hwnd, WM_AIMP_PROPERTY, new IntPtr(PROP_REPEAT | PROP_SET), new IntPtr(enabled ? 1 : 0));
    }

    public static bool GetShuffle()
    {
        var hwnd = RemoteWindow();
        if (hwnd == IntPtr.Zero) return false;
        return SendMessage(hwnd, WM_AIMP_PROPERTY, new IntPtr(PROP_SHUFFLE | PROP_GET), IntPtr.Zero) != IntPtr.Zero;
    }

    public static void SetShuffle(bool enabled)
    {
        var hwnd = RemoteWindow();
        if (hwnd == IntPtr.Zero) return;
        SendMessage(hwnd, WM_AIMP_PROPERTY, new IntPtr(PROP_SHUFFLE | PROP_SET), new IntPtr(enabled ? 1 : 0));
    }

    // Commands (WM_AIMP_COMMAND)

    private static void Cmd(int cmd, IntPtr lParam = default)
    {
        var hwnd = RemoteWindow();
        if (hwnd != IntPtr.Zero)
            SendMessage(hwnd, WM_AIMP_COMMAND, new IntPtr(cmd), lParam);
    }

    public static void Play()      => Cmd(CMD_PLAY);
    public static void PlayPause() => Cmd(CMD_PLAYPAUSE);
    public static void Pause()     => Cmd(CMD_PAUSE);
    public static void Stop()      => Cmd(CMD_STOP);
    public static void Next()      => Cmd(CMD_NEXT);
    public static void Previous()  => Cmd(CMD_PREV);

    public static void RequestAlbumArt(IntPtr receiverHwnd)
    {
        var hwnd = RemoteWindow();
        if (hwnd != IntPtr.Zero)
            SendMessage(hwnd, WM_AIMP_COMMAND, new IntPtr(CMD_GET_ALBUMART), receiverHwnd);
    }

    // Shared memory: track metadata

    public static AimpInfo? Read()
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(MapName);
            using var acc = mmf.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.Read);

            if (acc.ReadInt32(0x04) == 0) return null; // no track loaded

            long duration = acc.ReadUInt32(0x10);

            uint lenAlbum  = acc.ReadUInt32(0x28);
            uint lenArtist = acc.ReadUInt32(0x2C);
            uint lenDate   = acc.ReadUInt32(0x30);
            uint lenFile   = acc.ReadUInt32(0x34);
            uint lenGenre  = acc.ReadUInt32(0x38);
            uint lenTitle  = acc.ReadUInt32(0x3C);

            // strings are packed back to back starting at offset 0x58
            // (the fixed part of the struct is 88 bytes)
            long ptr = 0x58;
            string album  = ReadWStr(acc, ref ptr, lenAlbum);
            string artist = ReadWStr(acc, ref ptr, lenArtist);
            _             = ReadWStr(acc, ref ptr, lenDate);  // unused
            string file   = ReadWStr(acc, ref ptr, lenFile);
            _             = ReadWStr(acc, ref ptr, lenGenre); // unused
            string title  = ReadWStr(acc, ref ptr, lenTitle);

            return new AimpInfo
            {
                Title    = title,
                Artist   = artist,
                Album    = album,
                FilePath = file,
                Duration = duration,
            };
        }
        catch { return null; }
    }

    private static string ReadWStr(MemoryMappedViewAccessor acc, ref long pos, uint chars)
    {
        if (chars == 0) return "";
        long bytes = chars * 2L;
        if (pos + bytes > MapSize) return "";
        var buf = new byte[bytes];
        acc.ReadArray(pos, buf, 0, (int)bytes);
        pos += bytes;
        return Encoding.Unicode.GetString(buf);
    }
}
