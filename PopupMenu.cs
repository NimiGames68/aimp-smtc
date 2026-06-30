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
    private readonly ExitButton       _lblExit;

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

        // Exit button at the bottom - keyboard-navigable (TabStop)
        _lblExit = new ExitButton
        {
            Location = new Point(0, sliderY + 24 + 26),
            Size     = new Size(FixedSize.Width, 24),
        };
        _lblExit.Click += (_, _) => ExitClicked?.Invoke();

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
        _icon   = IconLoader.LoadPopupIcon(iconName);
        Size    = new Size(size, size);
        Cursor  = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void SetIcon(string iconName)
    {
        _icon = IconLoader.LoadPopupIcon(iconName);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e)   { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e)  { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_hover || Focused)
        {
            using var hoverBrush = new SolidBrush(PopupMenu.HoverBg);
            g.FillEllipse(hoverBrush, 0, 0, Width - 1, Height - 1);
        }

        int iconSize = (int)(Width * 0.46);
        var rect = new Rectangle((Width - iconSize) / 2, (Height - iconSize) / 2, iconSize, iconSize);
        g.DrawImage(_icon, rect);

        if (Focused)
        {
            using var pen = new Pen(PopupMenu.TextFg, 1.3f) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(pen, 1, 1, Width - 3, Height - 3);
        }
    }
}

internal sealed class Win11Cover : Control
{
    private Image?   _cover;
    private Bitmap?  _scaled; // pre-scaled to control size - avoids OOM in TextureBrush
    private const int Radius = 10;
    private static Color PlaceholderBg    => IconLoader.IsLightTheme() ? Color.FromArgb(230, 230, 230) : Color.FromArgb(45, 45, 48);
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
        _scaled?.Dispose();
        _cover  = img;
        _scaled = null;
        Invalidate();
    }

    private Bitmap? GetScaled()
    {
        if (_cover == null) return null;
        if (_scaled != null && _scaled.Width == Width && _scaled.Height == Height)
            return _scaled;

        // Rebuild scaled copy at control size (typically 84x84).
        // TextureBrush copies the entire source image into GDI+ memory - if
        // the raw album art is 1000x1000 that causes an OutOfMemoryException.
        // Pre-scaling to the actual display size once keeps both memory and
        // repaint cost low.
        _scaled?.Dispose();
        _scaled = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
        using var g2 = Graphics.FromImage(_scaled);
        g2.InterpolationMode  = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g2.SmoothingMode      = SmoothingMode.AntiAlias;
        g2.DrawImage(_cover, 0, 0, Width, Height);
        return _scaled;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        _scaled?.Dispose();
        _scaled = null; // invalidate cache when the control is resized
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Single path for both fill and border - SetClip() is always
        // hard-edged regardless of SmoothingMode and left a jagged corner;
        // FillPath properly antialiases the rounded rect edge.
        using var path = PopupMenu.RoundedRectPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius);

        var scaled = GetScaled();
        if (scaled != null)
        {
            using var brush = new TextureBrush(scaled, WrapMode.Clamp);
            g.FillPath(brush, path);
        }
        else
        {
            using var bgBrush = new SolidBrush(PlaceholderBg);
            g.FillPath(bgBrush, path);

            var icon = IconLoader.LoadPopupIcon("music-note");
            int iw = Width / 3, ih = Height / 3;
            var rect = new Rectangle((Width - iw) / 2, (Height - ih) / 2, iw, ih);

            using var ia = new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = 0.35f };
            ia.SetColorMatrix(matrix);
            g.DrawImage(icon, rect, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
        }

        using var pen = new Pen(CoverBorderColor, 1f);
        g.DrawPath(pen, path);
    }
}

internal sealed class InfoRow : Control
{
    private Image           _icon;
    private readonly string _iconName;
    private string          _text = "";
    private bool            _hover;
    private bool            _overflowing;
    private float           _scrollOffset;
    private static readonly Font TextFont = new("Segoe UI", 9f);

    private const int IconSize   = 15;
    private const int MarqueeGap = 30;

    private readonly System.Windows.Forms.Timer _marqueeTimer;

    public InfoRow(string iconName, int width)
    {
        _iconName = iconName;
        _icon   = IconLoader.LoadPopupIcon(iconName);
        Size    = new Size(width, 24);
        Cursor  = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer, true);

        _marqueeTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _marqueeTimer.Tick += (_, _) =>
        {
            int w = TextRenderer.MeasureText(_text, TextFont).Width;
            _scrollOffset += 1;
            if (_scrollOffset > w + MarqueeGap) _scrollOffset = 0;
            Invalidate();
        };
    }

    public void ReloadIcon()
    {
        _icon = IconLoader.LoadPopupIcon(_iconName);
        Invalidate();
    }

    public void SetText(string text)
    {
        _text = text;
        _scrollOffset = 0;
        int maxW = Width - (IconSize + 8);
        _overflowing = TextRenderer.MeasureText(_text, TextFont).Width > maxW;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        if (_overflowing) { _marqueeTimer.Start(); } // resume from current offset
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _marqueeTimer.Stop(); // keep _scrollOffset so next hover continues smoothly
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        if (_overflowing) _marqueeTimer.Start();
        Invalidate();
        base.OnGotFocus(e);
    }
    protected override void OnLostFocus(EventArgs e)
    {
        _marqueeTimer.Stop();
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        const int iconSize = 15;
        var iconRect = new Rectangle(0, (Height - iconSize) / 2, iconSize, iconSize);

        using var ia = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = 0.75f };
        ia.SetColorMatrix(matrix);
        g.DrawImage(_icon, iconRect, 0, 0, _icon.Width, _icon.Height, GraphicsUnit.Pixel, ia);

        int textX        = iconSize + 8;
        int maxTextWidth = Width - textX;
        var color        = (_hover || Focused) ? PopupMenu.TextFg : PopupMenu.SubTextFg;
        using var brush  = new SolidBrush(color);
        var fmt = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

        if (_overflowing && (_hover || Focused))
        {
            var oldClip = g.Clip;
            g.SetClip(new RectangleF(textX, 0, maxTextWidth, Height), CombineMode.Intersect);
            int tw    = TextRenderer.MeasureText(_text, TextFont).Width;
            float x1  = textX - _scrollOffset;
            g.DrawString(_text, TextFont, brush, new RectangleF(x1, 0, tw + 400, Height), fmt);
            float x2  = x1 + tw + MarqueeGap;
            g.DrawString(_text, TextFont, brush, new RectangleF(x2, 0, tw + 400, Height), fmt);
            g.SetClip(oldClip, CombineMode.Replace);
        }
        else
        {
            string shown = _overflowing ? Truncate(_text, TextFont, maxTextWidth) : _text;
            g.DrawString(shown, TextFont, brush, new RectangleF(textX, 0, maxTextWidth, Height), fmt);
        }

        if (Focused)
        {
            using var pen = new Pen(PopupMenu.TextFg, 1f) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _marqueeTimer.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Keyboard-navigable Exit button at the bottom of the popup.
/// Uses TabStop so it's reachable by Tab key; Enter/Space activates it.
/// </summary>
internal sealed class ExitButton : Control
{
    private bool _hover;
    private static readonly Font ExitFont = new("Segoe UI", 8.5f);

    public ExitButton()
    {
        Cursor  = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true;  Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e)   { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e)  { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var color = (_hover || Focused) ? PopupMenu.TextFg : PopupMenu.SubTextFg;
        using var brush = new SolidBrush(color);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("Exit", ExitFont, brush, ClientRectangle, fmt);

        if (Focused)
        {
            using var pen = new Pen(PopupMenu.SubTextFg, 1f) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }
}
