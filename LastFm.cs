using System;
using System.Diagnostics;

namespace AimpSmtc;

internal static class LastFm
{
    public static string ArtistUrl(string artist) =>
        $"https://www.last.fm/music/{Esc(artist)}";

    public static string AlbumUrl(string artist, string album) =>
        $"https://www.last.fm/music/{Esc(artist)}/{Esc(album)}";

    public static string TrackUrl(string artist, string title) =>
        $"https://www.last.fm/music/{Esc(artist)}/_/{Esc(title)}";

    private static string Esc(string s) =>
        Uri.EscapeDataString(s ?? "").Replace("%20", "+");

    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { /* no associated browser, etc. - fail silently */ }
    }
}
