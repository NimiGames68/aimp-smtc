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

        // AppMediaId is documented as "Windows 10 [desktop apps only]" - it's
        // the property meant for unpackaged Win32 apps to declare their
        // identity to the SMTC, as an alternative to AUMID + shortcut (which
        // only resolves reliably for MSIX-packaged apps).
        try { _smtc.DisplayUpdater.AppMediaId = AppIdentity.Aumid; }
        catch (Exception ex) { AppIdentity.Log($"AppMediaId failed: {ex.Message}"); }

        _smtc.ButtonPressed += OnButton;

        // Lambda here avoids referencing PlaybackPositionChangedEventArgs by
        // name, which isn't exported by the WinRT projection used in net8.
        _smtc.PlaybackPositionChangeRequested += (_, e) =>
        {
            long ms = (long)e.RequestedPlaybackPosition.TotalMilliseconds;
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
        }

        bool shuffle = AimpRemote.GetShuffle();
        if (_lastShuffle != shuffle)
        {
            _lastShuffle = shuffle;
            try { _smtc.ShuffleEnabled = shuffle; } catch { }
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
        }

        long posMs    = AimpRemote.GetPosition();
        long durMs    = Math.Clamp(info.Duration, 0, 24L * 3_600_000);
        long posClamp = Math.Clamp(posMs, 0, durMs > 0 ? durMs : long.MaxValue);

        try
        {
            if (durMs > 0)
            {
                _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime   = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    Position    = TimeSpan.FromMilliseconds(posClamp),
                    MaxSeekTime = TimeSpan.FromMilliseconds(durMs),
                    EndTime     = TimeSpan.FromMilliseconds(durMs),
                });
            }
            else
            {
                // Radio/stream with no known duration - clear the timeline so
                // the previous track's progress bar doesn't linger on screen.
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

        if (info.Title != _lastTitle)
        {
            _lastTitle  = info.Title;
            _coverDirty = false; // wait for the new cover to arrive

            // TrayApp requests fresh album art when it sees the track change
            // and forwards the result to OnAlbumArtReceived below.
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
                AimpRemote.PlayPause();
                break;
            case SystemMediaTransportControlsButton.Stop:     AimpRemote.Stop();     break;
            case SystemMediaTransportControlsButton.Next:     AimpRemote.Next();     break;
            case SystemMediaTransportControlsButton.Previous: AimpRemote.Previous(); break;
        }
    }

    // Called directly by TrayApp/PopupMenu (tray click, popup buttons).
    public void TogglePlayPause() => AimpRemote.PlayPause();
    public void Next()            => AimpRemote.Next();
    public void Previous()        => AimpRemote.Previous();
    public void StopPlayback()    => AimpRemote.Stop();

    public (long PositionMs, long DurationMs) GetTimeline()
        => (AimpRemote.GetPosition(), AimpRemote.GetDuration());

    public void Dispose()
    {
        _smtc.ButtonPressed -= OnButton;
        _smtc.IsEnabled = false;
    }
}
