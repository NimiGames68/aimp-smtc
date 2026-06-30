using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace AimpSmtc;

public sealed class SmtcBridge : IDisposable
{
    // BackgroundMediaPlayer is marked obsolete in the docs, but it's the
    // pattern foo_mediacontrol (a foobar2000 plugin, plain unpackaged Win32)
    // uses to get its app identity resolved correctly by the SMTC "Now
    // Playing" card. Switching to plain MediaPlayer() did not help, so we
    // stick with this.
    private readonly SystemMediaTransportControls _smtc;

    private AimpStatus _lastStatus = (AimpStatus)(-1);
    private string?    _lastTitle;
    private bool?       _lastShuffle;
    private bool?       _lastRepeat;

    // Debounce: SMTC can fire ButtonPressed more than once for a single click.
    private DateTime _lastBtn = DateTime.MinValue;
    private SystemMediaTransportControlsButton _lastBtnType;

    // Cover art received asynchronously via WM_COPYDATA (see OnAlbumArtReceived).
    private byte[]? _pendingCover;
    private bool    _coverDirty;

#pragma warning disable CS0618 // BackgroundMediaPlayer is obsolete, see note above

    public SmtcBridge()
    {
        _smtc = BackgroundMediaPlayer.Current.SystemMediaTransportControls;

        _smtc.IsEnabled         = true;
        _smtc.IsPlayEnabled     = true;
        _smtc.IsPauseEnabled    = true;
        _smtc.IsNextEnabled     = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.IsStopEnabled     = true;

        try { _smtc.DisplayUpdater.AppMediaId = AppIdentity.Aumid; }
        catch (Exception ex) { AppIdentity.Log($"AppMediaId failed: {ex.Message}"); }

        _smtc.ButtonPressed += OnButton;

        _smtc.PlaybackPositionChangeRequested += (_, e) =>
        {
            long ms = (long)e.RequestedPlaybackPosition.TotalMilliseconds;
            PlaybackLog.Log($"Seek: {PlaybackLog.FormatDuration(ms)}");
            AimpRemote.SetPosition(ms);
        };

        // Shuffle/repeat: the SMTC expects an explicit confirmation (writing
        // the property straight back) inside this handler. If we only update
        // our internal cache and let the next poll tick handle it, the poll
        // sees the cache already matches AIMP's value and skips the write -
        // without that confirmation, the SMTC times out after a few seconds
        // and snaps the toggle back on its own.
        _smtc.ShuffleEnabledChangeRequested += (_, e) =>
        {
            AimpRemote.SetShuffle(e.RequestedShuffleEnabled);
            _lastShuffle = e.RequestedShuffleEnabled;
            try { _smtc.ShuffleEnabled = e.RequestedShuffleEnabled; } catch { }
            PlaybackLog.Log($"Shuffle: {(e.RequestedShuffleEnabled ? "on" : "off")} (from SMTC)");
        };
        _smtc.AutoRepeatModeChangeRequested += (_, e) =>
        {
            // AIMP only has a single repeat on/off toggle - it doesn't
            // distinguish "repeat track" from "repeat list". We always
            // confirm as List (rather than echoing back whatever the user
            // picked) so it matches what the next poll will write anyway;
            // otherwise the icon could flicker between the two repeat modes.
            bool wantsRepeat = e.RequestedAutoRepeatMode != MediaPlaybackAutoRepeatMode.None;
            AimpRemote.SetRepeat(wantsRepeat);
            _lastRepeat = wantsRepeat;
            try
            {
                _smtc.AutoRepeatMode = wantsRepeat
                    ? MediaPlaybackAutoRepeatMode.List
                    : MediaPlaybackAutoRepeatMode.None;
            }
            catch { }
            PlaybackLog.Log($"Repeat: {(wantsRepeat ? "on" : "off")} (from SMTC)");
        };
    }

    public void Update(AimpInfo? info)
    {
        if (info == null || !AimpRemote.IsRunning())
        {
            if (_smtc.IsEnabled)
            {
                _smtc.IsEnabled = false;
                _smtc.DisplayUpdater.ClearAll();
                _smtc.DisplayUpdater.Update();
                PlaybackLog.Log("AIMP closed");
            }
            _lastStatus  = (AimpStatus)(-1);
            _lastTitle   = null;
            _lastShuffle = null;
            _lastRepeat  = null;
            return;
        }

        if (!_smtc.IsEnabled)
        {
            _smtc.IsEnabled         = true;
            _smtc.IsPlayEnabled     = true;
            _smtc.IsPauseEnabled    = true;
            _smtc.IsNextEnabled     = true;
            _smtc.IsPreviousEnabled = true;
            _smtc.IsStopEnabled     = true;
            try { _smtc.DisplayUpdater.AppMediaId = AppIdentity.Aumid; } catch { }
        }

        bool trackChanged = info.Title != _lastTitle;

        var status = AimpRemote.GetState();
        if (status != _lastStatus)
        {
            _lastStatus = status;
            _smtc.PlaybackStatus = status switch
            {
                AimpStatus.Playing => MediaPlaybackStatus.Playing,
                AimpStatus.Paused  => MediaPlaybackStatus.Paused,
                _                  => MediaPlaybackStatus.Stopped,
            };
            // suppress "Playing" log when it's just a new track starting
            // - that event is already covered by the "Now playing:" line below
            if (!(status == AimpStatus.Playing && trackChanged))
            {
                PlaybackLog.Log(status switch
                {
                    AimpStatus.Playing => "Resumed",
                    AimpStatus.Paused  => "Paused",
                    AimpStatus.Stopped => "Stopped",
                    _                  => $"State: {status}",
                });
            }
        }

        bool shuffle = AimpRemote.GetShuffle();
        if (_lastShuffle != shuffle)
        {
            _lastShuffle = shuffle;
            try { _smtc.ShuffleEnabled = shuffle; } catch { }
            PlaybackLog.Log($"Shuffle: {(shuffle ? "on" : "off")}");
        }

        bool repeat = AimpRemote.GetRepeat();
        if (_lastRepeat != repeat)
        {
            _lastRepeat = repeat;
            try
            {
                _smtc.AutoRepeatMode = repeat
                    ? MediaPlaybackAutoRepeatMode.List
                    : MediaPlaybackAutoRepeatMode.None;
            }
            catch { }
            PlaybackLog.Log($"Repeat: {(repeat ? "on" : "off")}");
        }

        long posMs = AimpRemote.GetPosition();

        // AIMP doesn't zero out the Duration field in the memory map when
        // switching to a radio stream - it keeps the previous track's value.
        // Use two signals: URL scheme in FilePath, AND the live WM_AIMP_PROPERTY
        // duration (which is more reliable than the memory-map value).
        bool isUrlStream = info.FilePath.StartsWith("http://",  StringComparison.OrdinalIgnoreCase)
                        || info.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        || info.FilePath.StartsWith("rtsp://",  StringComparison.OrdinalIgnoreCase)
                        || info.FilePath.StartsWith("mms://",   StringComparison.OrdinalIgnoreCase);
        long liveDur = AimpRemote.GetDuration(); // WM_AIMP_PROPERTY - clears to 0 for streams
        bool isStream = isUrlStream || liveDur == 0;
        _isStream = isStream;
        long durMs = isStream ? 0 : Math.Clamp(liveDur, 0, 24L * 3_600_000);

        // AIMP returns 0xFFFFFFFF (~uint.MaxValue) as a sentinel value
        // when position is meaningless (e.g. paused on a radio stream).
        // Treat anything over 24 hours as invalid and fall back to 0.
        const long MaxValidMs = 24L * 3_600_000;
        if (posMs < 0 || posMs > MaxValidMs) posMs = 0;

        long posClamp = durMs > 0 ? Math.Clamp(posMs, 0, durMs) : posMs;

        try
        {
            if (durMs > 0)
            {
                // Regular track: full seek bar with known start and end.
                _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime   = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    Position    = TimeSpan.FromMilliseconds(posClamp),
                    MaxSeekTime = TimeSpan.FromMilliseconds(durMs),
                    EndTime     = TimeSpan.FromMilliseconds(durMs),
                });
            }
            else if (trackChanged)
            {
                // Stream/radio and the track just changed: explicitly clear
                // the old timeline so the previous song's duration doesn't
                // linger. We only do this once (on track change) to avoid
                // Discord resetting its counter on every 150ms poll tick.
                _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime   = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    Position    = TimeSpan.Zero,
                    MaxSeekTime = TimeSpan.Zero,
                    EndTime     = TimeSpan.Zero,
                });
            }
        }
        catch { }

        if (trackChanged)
        {
            _lastTitle  = info.Title;
            _coverDirty = false;

            string artist = string.IsNullOrWhiteSpace(info.Artist) ? "Unknown artist" : info.Artist;
            string album  = string.IsNullOrWhiteSpace(info.Album)  ? "Unknown album"  : info.Album;
            PlaybackLog.Log(
                $"Now playing: \"{info.Title}\" by {artist} ({album}), " +
                $"duration {PlaybackLog.FormatDuration(info.Duration)}");

            _ = PushMetadataAsync(info, null);
        }

        if (_coverDirty)
        {
            _coverDirty = false;
            _ = UpdateCoverAsync(_pendingCover);
        }
    }

    public void OnAlbumArtReceived(byte[] data)
    {
        _pendingCover = data.Length > 0 ? data : null;
        _coverDirty   = true;
    }

    private async Task PushMetadataAsync(AimpInfo info, byte[]? cover)
    {
        try
        {
            var upd = _smtc.DisplayUpdater;
            upd.Type = MediaPlaybackType.Music;
            upd.MusicProperties.Title      = info.Title;
            upd.MusicProperties.Artist     = info.Artist;
            upd.MusicProperties.AlbumTitle = info.Album;
            upd.Thumbnail = await BuildThumbnailAsync(cover);
            upd.Update();
        }
        catch { }
    }

    private async Task UpdateCoverAsync(byte[]? cover)
    {
        try
        {
            var upd = _smtc.DisplayUpdater;
            upd.Thumbnail = await BuildThumbnailAsync(cover);
            upd.Update();
        }
        catch { }
    }

    private static async Task<RandomAccessStreamReference?> BuildThumbnailAsync(byte[]? data)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            var ms = new InMemoryRandomAccessStream();
            await ms.WriteAsync(data.AsBuffer());
            ms.Seek(0);
            return RandomAccessStreamReference.CreateFromStream(ms) as RandomAccessStreamReference;
        }
        catch { return null; }
    }

    // True when the current source is a URL stream (radio, etc.).
    // Stored so GetTimeline() can apply the same duration=0 rule.
    private bool _isStream;

    public bool IsPlaying => _lastStatus == AimpStatus.Playing;

    private void OnButton(SystemMediaTransportControls _,
                          SystemMediaTransportControlsButtonPressedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (e.Button == _lastBtnType && (now - _lastBtn).TotalMilliseconds < 500) return;
        _lastBtn = now; _lastBtnType = e.Button;

        switch (e.Button)
        {
            case SystemMediaTransportControlsButton.Play:
            case SystemMediaTransportControlsButton.Pause:
                TogglePlayPause();
                break;
            case SystemMediaTransportControlsButton.Stop:     StopPlayback(); break;
            case SystemMediaTransportControlsButton.Next:     Next();         break;
            case SystemMediaTransportControlsButton.Previous: Previous();     break;
        }
    }

    public void TogglePlayPause() { PlaybackLog.Log("Action: play/pause"); AimpRemote.PlayPause(); }
    public void Next()            { PlaybackLog.Log("Action: next");       AimpRemote.Next(); }
    public void Previous()        { PlaybackLog.Log("Action: previous");   AimpRemote.Previous(); }
    public void StopPlayback()    { PlaybackLog.Log("Action: stop");       AimpRemote.Stop(); }

    public (long PositionMs, long DurationMs) GetTimeline()
    {
        long pos = AimpRemote.GetPosition();
        long dur = _isStream ? 0 : AimpRemote.GetDuration();
        const long MaxValidMs = 24L * 3_600_000;
        if (pos < 0 || pos > MaxValidMs) pos = 0;
        return (pos, Math.Clamp(dur, 0, MaxValidMs));
    }

    public void Dispose()
    {
        _smtc.ButtonPressed -= OnButton;
        _smtc.IsEnabled = false;
    }
}
