using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace AimpSmtc;

internal static class IconLoader
{
    public static Icon LoadAppIcon()
    {
        try
        {
            string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            return Icon.ExtractAssociatedIcon(exe) ?? SystemIcons.Application;
        }
        catch { return SystemIcons.Application; }
    }

    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("SystemUsesLightTheme") is int v) return v != 0;
        }
        catch { }
        return false;
    }

    public static Icon LoadTrayIcon()
    {
        string name = IsLightTheme()
            ? "AimpSmtc.tray_light.png"
            : "AimpSmtc.tray_dark.png";

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Resource not found: {name}");

        using var bmp = (Bitmap)Image.FromStream(stream);
        return BitmapToIcon(bmp);
    }

    // Popup icons (prev/play/pause/next/album/artist/music-note): white,
    // transparent background, designed for the popup's dark theme.
    private static readonly Dictionary<string, Image> _iconCache = new();

    public static Image LoadPopupIcon(string name)
    {
        bool isLight = IsLightTheme();
        string cacheKey = $"{name}_{(isLight ? "light" : "dark")}";
        if (_iconCache.TryGetValue(cacheKey, out var cached)) return cached;

        var asm = Assembly.GetExecutingAssembly();
        string resName = $"AimpSmtc.Icons.{name}.png";
        using var stream = asm.GetManifestResourceStream(resName)
            ?? throw new InvalidOperationException($"Resource not found: {resName}");

        var img = Image.FromStream(stream);

        if (isLight)
        {
            var inverted = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (var bmp = new Bitmap(img))
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        Color p = bmp.GetPixel(x, y);
                        // Make pixel black but keep the original alpha channel
                        inverted.SetPixel(x, y, Color.FromArgb(p.A, 0, 0, 0));
                    }
                }
            }
            img.Dispose();
            img = inverted;
        }

        _iconCache[cacheKey] = img;
        return img;
    }

    private static Icon BitmapToIcon(Bitmap bmp)
    {
        using var resized = new Bitmap(bmp, new Size(32, 32)); // standard tray icon size

        using var ms = new MemoryStream();
        WriteIco(ms, resized);
        ms.Seek(0, SeekOrigin.Begin);
        return new Icon(ms, new Size(32, 32));
    }

    private static void WriteIco(Stream s, Bitmap bmp)
    {
        using var png = new MemoryStream();
        bmp.Save(png, ImageFormat.Png);
        byte[] pngBytes = png.ToArray();

        // Minimal single-image ICO container wrapping a 32-bit PNG, written
        // by hand because System.Drawing has no built-in PNG-in-ICO encoder.
        using var w = new BinaryWriter(s, System.Text.Encoding.UTF8, leaveOpen: true);

        // ICONDIR
        w.Write((short)0); // reserved
        w.Write((short)1); // type = icon
        w.Write((short)1); // image count

        // ICONDIRENTRY
        w.Write((byte)32);  // width
        w.Write((byte)32);  // height
        w.Write((byte)0);   // color count (0 = no palette, true color)
        w.Write((byte)0);   // reserved
        w.Write((short)1);  // color planes
        w.Write((short)32); // bits per pixel
        w.Write(pngBytes.Length);
        w.Write(22); // offset to image data (6-byte header + 16-byte entry)

        w.Write(pngBytes);
    }
}
