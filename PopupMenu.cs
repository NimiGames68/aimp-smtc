using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AimpSmtc;

internal sealed class PopupMenu : Form
{
    private const int CornerRadius = 12;
    private static readonly Size FixedSize = new(320, 264);

    private static Color BgColor      => IconLoader.IsLightTheme() ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
    private static Color BorderColor  => IconLoader.IsLightTheme() ? Color.FromArgb(220, 220, 220) : Color.FromArgb(55, 55, 55);
    private static Color TextColor    => IconLoader.IsLightTheme() ? Color.FromArgb(20, 20, 20) : Color.FromArgb(235, 235, 235);
    private static Color SubTextColor => IconLoader.IsLightTheme() ? Color.FromArgb(100, 100, 100) : Color.FromArgb(150, 150, 150);
    private static Color HoverColor   => IconLoader.IsLightTheme() ? Color.FromArgb(230, 230, 230) : Color.FromArgb(48, 48, 48);

    private readonly Win11IconButton _btnPrev;
    private readonly Win11IconButton _btnPlayPause;
    private readonly Win11IconButton _btnNext;
    private readonly Win11Cover      _cover;
    private readonly InfoRow         _rowTitle;
    private readonly InfoRow         _rowArtist;
    private readonly InfoRow         _rowAlbum;
    private readonly Win11Slider     _slider;
    private readonly Label           _timeCurrent;
    private readonly Label           _timeTotal;
    private readonly Label           _lblTitle;
    private readonly Label           _lblVersion;
    private readonly Label           _lblExit;

    private string _curTitle = "", _curArtist = "", _curAlbum = "";
    private long   _curDurationMs;
    private byte[]? _lastCoverBytes;

    public event Action? PlayPauseClicked;
    public event Action? NextClicked;
    public event Action? PreviousClicked;
    public event Action? ExitClicked;

    public event Action<double>? SeekRequested;

    public PopupMenu()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = false;
        BackColor       = BgColor;
        Size            = FixedSize;
        MinimumSize     = FixedSize;
        MaximumSize     = FixedSize; // fixed size, by design
        TopMost         = true;
        DoubleBuffered  = true;
        Font            = new Font("Segoe UI", 9f);

        // Header
        _lblTitle = new Label
        {
            Text      = "AIMP SMTC",
            ForeColor = TextColor,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            AutoSize  = false,
            Cursor    = Cursors.Hand,
            Location  = new Point(14, 10),
            Size      = new Size(180, 18),
        };
        _lblTitle.MouseEnter += (_, _) => _lblTitle.ForeColor = IconLoader.IsLightTheme() ? Color.FromArgb(0, 100, 200) : Color.FromArgb(150, 190, 255);
        _lblTitle.MouseLeave += (_, _) => _lblTitle.ForeColor = TextColor;
        _lblTitle.Click       += (_, _) => LastFm.Open("https://github.com/NimiGames68/aimp-smtc");

        _lblVersion = new Label
        {
            Text      = "v1.0",
            ForeColor = SubTextColor,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 8f),
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleRight,
            Location  = new Point(FixedSize.Width - 16 - 50, 12),
            Size      = new Size(50, 16),
        };

        // Transport controls
        const int btnSize = 40, gap = 14;
        int controlsWidth = btnSize * 3 + gap * 2;
        int controlsX = (FixedSize.Width - controlsWidth) / 2;
        int controlsY = 34;

        _btnPrev = new Win11IconButton("previous", btnSize)
        { Location = new Point(controlsX, controlsY) };
        _btnPlayPause = new Win11IconButton("pause", btnSize)
        { Location = new Point(controlsX + btnSize + gap, controlsY) };
        _btnNext = new Win11IconButton("next", btnSize)
        { Location = new Point(controlsX + (btnSize + gap) * 2, controlsY) };

        _btnPrev.Click      += (_, _) => PreviousClicked?.Invoke();
        _btnPlayPause.Click += (_, _) => PlayPauseClicked?.Invoke();
        _btnNext.Click      += (_, _) => NextClicked?.Invoke();

        // Cover art + info rows
        const int coverSize = 84;
        const int coverX = 16, coverY = 86;
        _cover = new Win11Cover { Location = new Point(coverX, coverY), Size = new Size(coverSize, coverSize) };

        int infoX = coverX + coverSize + 12;
        int infoWidth = FixedSize.Width - infoX - 16;

        _rowTitle  = new InfoRow("music-note", infoWidth) { Location = new Point(infoX, coverY) };
        _rowArtist = new InfoRow("artist",     infoWidth) { Location = new Point(infoX, coverY + 28) };
        _rowAlbum  = new InfoRow("album",      infoWidth) { Location = new Point(infoX, coverY + 56) };

        _rowTitle.Click  += (_, _) => { if (HasTrack) LastFm.Open(LastFm.TrackUrl(_curArtist, _curTitle)); };
        _rowArtist.Click += (_, _) => { if (HasArtist) LastFm.Open(LastFm.ArtistUrl(_curArtist)); };
        _rowAlbum.Click  += (_, _) => { if (HasAlbum) LastFm.Open(LastFm.AlbumUrl(_curArtist, _curAlbum)); };

        // Progress slider + time labels
        const int sliderY = 180;
        _slider = new Win11Slider
        {
            Location = new Point(16, sliderY),
            Size     = new Size(FixedSize.Width - 32, 22),
        };
        _slider.Seeked += ratio => SeekRequested?.Invoke(ratio);

        _timeCurrent = new Label
        {
            Text = "0:00", ForeColor = SubTextColor, BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8f), AutoSize = false,
            Location = new Point(16, sliderY + 24), Size = new Size(80, 16),
        };
        _timeTotal = new Label
        {
            Text = "0:00", ForeColor = SubTextColor, BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8f), AutoSize = false, TextAlign = ContentAlignment.MiddleRight,
            Location = new Point(FixedSize.Width - 16 - 80, sliderY + 24), Size = new Size(80, 16),
        };

        // Exit link, at the bottom below the time row
        _lblExit = new Label
        {
            Text      = "Exit",
            ForeColor = SubTextColor,
            BackColor = Color.Transparent,
            Font      = new Font("Segoe UI", 8.5f),
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor    = Cursors.Hand,
            Location  = new Point(0, sliderY + 24 + 30),
            Size      = new Size(FixedSize.Width, 20),
        };
        _lblExit.MouseEnter += (_, _) => _lblExit.ForeColor = TextColor;
        _lblExit.MouseLeave += (_, _) => _lblExit.ForeColor = SubTextColor;
        _lblExit.Click      += (_, _) => ExitClicked?.Invoke();

        Controls.AddRange(new Control[]
        {
            _lblTitle, _lblVersion,
            _btnPrev, _btnPlayPause, _btnNext,
            _cover, _rowTitle, _rowArtist, _rowAlbum,
            _slider, _timeCurrent, _timeTotal, _lblExit,
        });

        Deactivate += (_, _) => Hide();
    }

    private bool HasTrack  => !string.IsNullOrWhiteSpace(_curTitle) && !string.IsNullOrWhiteSpace(_curArtist);
    private bool HasArtist => !string.IsNullOrWhiteSpace(_curArtist);
    private bool HasAlbum  => !string.IsNullOrWhiteSpace(_curArtist) && !string.IsNullOrWhiteSpace(_curAlbum);

    public void ApplyTheme()
    {
        BackColor = BgColor;
        _lblTitle.ForeColor = TextColor;
        _lblVersion.ForeColor = SubTextColor;
        _lblExit.ForeColor = SubTextColor;
        _timeCurrent.ForeColor = SubTextColor;
        _timeTotal.ForeColor = SubTextColor;

        _btnPrev.SetIcon("previous");
        _btnPlayPause.SetIcon(_isPlaying ? "pause" : "play");
        _btnNext.SetIcon("next");
        
        _rowTitle.ReloadIcon();
        _rowArtist.ReloadIcon();
        _rowAlbum.ReloadIcon();

        _btnPrev.Invalidate();
        _btnPlayPause.Invalidate();
        _btnNext.Invalidate();
        _cover.Invalidate();
        _rowTitle.Invalidate();
        _rowArtist.Invalidate();
        _rowAlbum.Invalidate();
        _slider.Invalidate();
        Invalidate();
    }

    private bool _isPlaying;

    // Public API

    public void SetPlayingState(bool playing)
    {
        _isPlaying = playing;
        _btnPlayPause.SetIcon(playing ? "pause" : "play");
    }

    public void SetTrackInfo(string? titleText, string? artist, string? album)
    {
        _curTitle  = titleText ?? "";
        _curArtist = artist    ?? "";
        _curAlbum  = album     ?? "";

        _rowTitle.SetText(string.IsNullOrWhiteSpace(_curTitle) ? "-" : _curTitle);
        _rowArtist.SetText(string.IsNullOrWhiteSpace(_curArtist) ? "-" : _curArtist);
        _rowAlbum.SetText(string.IsNullOrWhiteSpace(_curAlbum) ? "-" : _curAlbum);
    }

    public void SetCoverArt(byte[]? data)
    {
        if (ReferenceEquals(data, _lastCoverBytes)) return; // already decoded
        _lastCoverBytes = data;

        if (data == null || data.Length == 0) { _cover.SetCover(null); return; }
        try
        {
            using var ms = new MemoryStream(data);
            var img = Image.FromStream(ms);
            _cover.SetCover(img);
        }
        catch { _cover.SetCover(null); }
    }

    public void SetTimeline(long positionMs, long durationMs)
    {
        _curDurationMs = durationMs;
        _slider.Value  = durationMs > 0 ? (double)positionMs / durationMs : 0.0;
        _timeCurrent.Text = FormatTime(positionMs);
        _timeTotal.Text   = FormatTime(durationMs);
    }

    private static string FormatTime(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }

    public void ShowAt(Point anchor)
    {
        var area = Screen.FromPoint(anchor).WorkingArea;

        int x = Math.Min(anchor.X, area.Right - Width);
        int y = anchor.Y - Height;
        if (y < area.Top) y = anchor.Y;

        x = Math.Max(area.Left, x);
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - Height));

        Location = new Point(x, y);
        Show();
        Activate();
    }

    // Rounded corners

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRoundedRegion();
        TryEnableDwmRoundCorners();
    }

    private void ApplyRoundedRegion()
    {
        using var path = RoundedRectPath(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(BorderColor, 1);
        using var path = RoundedRectPath(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        e.Graphics.DrawPath(pen, path);
    }

    internal static GraphicsPath RoundedRectPath(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseAllFigures();
        return path;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWC_ROUND = 2;

    private void TryEnableDwmRoundCorners()
    {
        try
        {
            int pref = DWMWC_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { /* Windows 10 doesn't support this attribute - falls back to the GDI region only */ }
    }

    internal static Color HoverBg   => HoverColor;
    internal static Color TextFg    => TextColor;
    internal static Color SubTextFg => SubTextColor;
}

// Popup sub-controls

internal sealed class Win11IconButton : Control
{
    private Image _icon;
    private bool  _hover;

    public Win11IconButton(string iconName, int size)
    {
        _icon = IconLoader.LoadPopupIcon(iconName);
        Size  = new Size(size, size);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer, true);
        // No explicit BackColor: a plain Control doesn't support
        // Color.Transparent (only Label does), so leaving it unset makes
        // it inherit the parent's BackColor through the ambient property.
    }

    public void SetIcon(string iconName)
    {
        _icon = IconLoader.LoadPopupIcon(iconName);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_hover)
        {
            using var hoverBrush = new SolidBrush(PopupMenu.HoverBg);
            g.FillEllipse(hoverBrush, 0, 0, Width - 1, Height - 1);
        }

        int iconSize = (int)(Width * 0.46);
        var rect = new Rectangle((Width - iconSize) / 2, (Height - iconSize) / 2, iconSize, iconSize);
        g.DrawImage(_icon, rect);
    }
}

internal sealed class Win11Cover : Control
{
    private Image? _cover;
    private const int Radius = 10;
    private static Color PlaceholderBg => IconLoader.IsLightTheme() ? Color.FromArgb(230, 230, 230) : Color.FromArgb(45, 45, 48);
    private static Color CoverBorderColor => IconLoader.IsLightTheme() ? Color.FromArgb(200, 200, 200) : Color.FromArgb(60, 60, 64);

    public Win11Cover()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void SetCover(Image? img)
    {
        if (ReferenceEquals(img, _cover)) return;
        _cover?.Dispose();
        _cover = img;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var clipPath = PopupMenu.RoundedRectPath(ClientRectangle, Radius);
        var oldClip = g.Clip;
        g.SetClip(clipPath, CombineMode.Replace);

        using var bgBrush = new SolidBrush(PlaceholderBg);
        g.FillRectangle(bgBrush, ClientRectangle);

        if (_cover != null)
        {
            g.DrawImage(_cover, ClientRectangle);
        }
        else
        {
            var icon = IconLoader.LoadPopupIcon("music-note");
            int iw = Width / 3, ih = Height / 3;
            var rect = new Rectangle((Width - iw) / 2, (Height - ih) / 2, iw, ih);

            using var ia = new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = 0.35f }; // dim the placeholder icon
            ia.SetColorMatrix(matrix);
            g.DrawImage(icon, rect, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
        }

        g.SetClip(oldClip, CombineMode.Replace);

        using var borderPath = PopupMenu.RoundedRectPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var pen = new Pen(CoverBorderColor, 1f);
        g.DrawPath(pen, borderPath);
    }
}

internal sealed class InfoRow : Control
{
    private Image          _icon;
    private readonly string _iconName;
    private string         _text = "";
    private bool            _hover;
    private static readonly Font TextFont = new("Segoe UI", 9f);

    public InfoRow(string iconName, int width)
    {
        _iconName = iconName;
        _icon = IconLoader.LoadPopupIcon(iconName);
        Size  = new Size(width, 24);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer, true);
        // No explicit BackColor - see the note in Win11IconButton.
    }

    public void ReloadIcon()
    {
        _icon = IconLoader.LoadPopupIcon(_iconName);
        Invalidate();
    }

    public void SetText(string text)
    {
        _text = text;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        const int iconSize = 15;
        var iconRect = new Rectangle(0, (Height - iconSize) / 2, iconSize, iconSize);

        using var ia = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = 0.75f };
        ia.SetColorMatrix(matrix);
        g.DrawImage(_icon, iconRect, 0, 0, _icon.Width, _icon.Height, GraphicsUnit.Pixel, ia);

        int textX = iconSize + 8;
        int maxTextWidth = Width - textX;
        string shown = Truncate(_text, TextFont, maxTextWidth);

        var color = _hover ? PopupMenu.TextFg : PopupMenu.SubTextFg;
        using var brush = new SolidBrush(color);
        var textRect = new RectangleF(textX, 0, maxTextWidth, Height);
        var fmt = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None };
        g.DrawString(shown, TextFont, brush, textRect, fmt);
    }

    private static string Truncate(string text, Font font, int maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (TextRenderer.MeasureText(text, font).Width <= maxWidth) return text;

        const string ellipsis = "…";
        string result = text;
        while (result.Length > 1)
        {
            result = result[..^1];
            if (TextRenderer.MeasureText(result + ellipsis, font).Width <= maxWidth)
                return result + ellipsis;
        }
        return ellipsis;
    }
}
