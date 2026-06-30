using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AimpSmtc;

internal sealed class TrayApp : ApplicationContext, IDisposable
{
    private readonly SmtcBridge _smtc;
    private readonly NotifyIcon _tray;
    private readonly PopupMenu  _popup;
    private readonly System.Windows.Forms.Timer _timer;

    // Hidden window used to receive album art from AIMP over WM_COPYDATA.
    private readonly AlbumArtReceiver _artReceiver;

    // Tracks the current track so we know when to request fresh cover art.
    private string? _lastCoverTitle;
    private byte[]?  _lastCoverBytes;
    private AimpInfo? _lastInfo;

    public TrayApp()
    {
        _artReceiver = new AlbumArtReceiver(OnAlbumArtReceived);

        _smtc = new SmtcBridge();

        _popup = new PopupMenu();
        _popup.PlayPauseClicked += () => _smtc.TogglePlayPause();
        _popup.NextClicked      += () => _smtc.Next();
        _popup.PreviousClicked  += () => _smtc.Previous();
        _popup.ExitClicked      += () => { _timer.Stop(); Application.Exit(); };
        _popup.SeekRequested    += ratio =>
        {
            var (_, dur) = _smtc.GetTimeline();
            if (dur > 0) AimpRemote.SetPosition((long)(ratio * dur));
        };

        _tray = new NotifyIcon
        {
            Icon    = IconLoader.LoadTrayIcon(),
            Text    = "AIMP SMTC",
            Visible = true,
        };

        _tray.MouseClick += (_, _) =>
        {
            RefreshPopup();
            _popup.ShowAt(Cursor.Position);
        };

        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

        _timer = new System.Windows.Forms.Timer { Interval = 150, Enabled = true };
        _timer.Tick += Poll;
    }

    private void Poll(object? s, EventArgs e)
    {
        var info = AimpRemote.Read();
        _lastInfo = info;
        _smtc.Update(info);

        if (info != null && info.Title != _lastCoverTitle)
        {
            _lastCoverTitle = info.Title;
            _lastCoverBytes = null; // cleared until the new cover arrives
            AimpRemote.RequestAlbumArt(_artReceiver.Handle);
        }

        if (_popup.Visible) RefreshPopup();
    }

    private void RefreshPopup()
    {
        _popup.SetPlayingState(_smtc.IsPlaying);
        _popup.SetTrackInfo(_lastInfo?.Title, _lastInfo?.Artist, _lastInfo?.Album);
        _popup.SetCoverArt(_lastCoverBytes);
        var (pos, dur) = _smtc.GetTimeline();
        _popup.SetTimeline(pos, dur);
    }

    private void OnAlbumArtReceived(byte[] data)
    {
        _lastCoverBytes = data;
        _smtc.OnAlbumArtReceived(data);
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General &&
            e.Category != UserPreferenceCategory.VisualStyle &&
            e.Category != UserPreferenceCategory.Color) return;
        try { _tray.Icon = IconLoader.LoadTrayIcon(); } catch { }
        try { _popup.ApplyTheme(); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            _timer.Dispose();
            _smtc.Dispose();
            _artReceiver.Dispose();
            _popup.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class AlbumArtReceiver : NativeWindow, IDisposable
{
    private readonly Action<byte[]> _callback;

    private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const int WM_SETICON          = 0x0080;
    private const int ICON_SMALL          = 0;
    private const int ICON_BIG            = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int    cbData;
        public IntPtr lpData;
    }

    public AlbumArtReceiver(Action<byte[]> callback)
    {
        _callback = callback;

        // A "real" top-level window - proper style, title and class, just
        // like foobar2000 has - but never actually shown (no WS_VISIBLE).
        CreateHandle(new CreateParams
        {
            Caption   = "AIMP SMTC",
            ClassName = null, // let WinForms generate its own window class
            Style     = WS_OVERLAPPEDWINDOW,
            X = -32000, Y = -32000, // off-screen, redundant with staying hidden
            Width = 1, Height = 1,
        });

        try
        {
            using var icon = IconLoader.LoadAppIcon();
            SendMessage(Handle, WM_SETICON, (IntPtr)ICON_BIG,   icon.Handle);
            SendMessage(Handle, WM_SETICON, (IntPtr)ICON_SMALL, icon.Handle);
        }
        catch { /* not critical - the hidden window just stays icon-less */ }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x004A /*WM_COPYDATA*/)
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(m.LParam);
            if ((int)cds.dwData == AimpRemote.WM_AIMP_COPYDATA_ALBUMART_ID && cds.cbData > 0)
            {
                var data = new byte[cds.cbData];
                Marshal.Copy(cds.lpData, data, 0, data.Length);
                _callback(data);
            }
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
