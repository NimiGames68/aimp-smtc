using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AimpSmtc;

internal sealed class Win11Slider : Control
{
    private static Color TrackColor => IconLoader.IsLightTheme() ? Color.FromArgb(200, 200, 200) : Color.FromArgb(90, 92, 100);
    private static Color HaloColor  => IconLoader.IsLightTheme() ? Color.FromArgb(70, 0, 120, 215) : Color.FromArgb(70, 200, 202, 215); // translucent
    private static Color CoreColor  => IconLoader.IsLightTheme() ? Color.FromArgb(0, 120, 215) : Color.FromArgb(235, 236, 242);
    private static Color CoreBorder => IconLoader.IsLightTheme() ? Color.FromArgb(0, 120, 215) : Color.FromArgb(140, 142, 155);

    private const int TrackThickness = 2;
    private const int CoreRadius     = 5;
    private const int HaloRadius     = 9;

    // Padding equals the halo radius so the thumb's halo never gets
    // clipped by the control's bounds at either end of the track.
    private const int Padding = HaloRadius;

    private double _value; // 0.0 .. 1.0
    private bool   _dragging;

    public event Action<double>? Seeked;

    public Win11Slider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor  = Cursors.Hand;
        TabStop = true;
        Height  = HaloRadius * 2 + 4;
    }

    public double Value
    {
        get => _value;
        set
        {
            if (_dragging) return; // don't fight the user's gesture
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(clamped - _value) < 0.0001) return;
            _value = clamped;
            Invalidate();
        }
    }

    private int TrackWidth => Math.Max(1, Width - Padding * 2);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _dragging = true;
        UpdateFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) UpdateFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;
        UpdateFromMouse(e.X);
        Seeked?.Invoke(_value);
    }

    private void UpdateFromMouse(int x)
    {
        double ratio = (double)(x - Padding) / TrackWidth;
        _value = Math.Clamp(ratio, 0.0, 1.0);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        const double step = 0.02;
        switch (e.KeyCode)
        {
            case Keys.Left:  _value = Math.Clamp(_value - step, 0, 1); break;
            case Keys.Right: _value = Math.Clamp(_value + step, 0, 1); break;
            case Keys.Home:  _value = 0; break;
            case Keys.End:   _value = 1; break;
            default: return;
        }
        Seeked?.Invoke(_value);
        Invalidate();
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e)  { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int midY   = Height / 2;
        int thumbX = Padding + (int)(_value * TrackWidth);

        using var trackBrush = new SolidBrush(TrackColor);
        g.FillRectangle(trackBrush, Padding, midY - TrackThickness / 2, TrackWidth, TrackThickness);

        var haloRect = new Rectangle(thumbX - HaloRadius, midY - HaloRadius,
                                      HaloRadius * 2, HaloRadius * 2);
        using var haloBrush = new SolidBrush(HaloColor);
        g.FillEllipse(haloBrush, haloRect);

        var coreRect = new Rectangle(thumbX - CoreRadius, midY - CoreRadius,
                                      CoreRadius * 2, CoreRadius * 2);
        using var coreBrush = new SolidBrush(CoreColor);
        g.FillEllipse(coreBrush, coreRect);
        using var corePen = new Pen(CoreBorder, 1f);
        g.DrawEllipse(corePen, coreRect);

        if (Focused)
        {
            using var fp = new Pen(Color.FromArgb(120, 200, 210, 255), 1f) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(fp, 0, 0, Width - 1, Height - 1);
        }
    }
}
