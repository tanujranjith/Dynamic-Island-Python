using DynamicIsland.Windows.Infrastructure;
using DynamicIsland.Windows.Models;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class MediaPresentationTests
{
    private static readonly byte[] SharedArtwork = new byte[] { 1, 2, 3 };
    private static MediaInfo Base => new()
    {
        Title = "Song A",
        Artist = "Artist X",
        Album = "Album 1",
        SourceAppId = "Spotify.exe",
        SourceAppName = "Spotify",
        PlaybackState = MediaPlaybackState.Playing,
        CanPlayPause = true, CanPrevious = true, CanNext = true, CanSeek = true,
        Duration = TimeSpan.FromSeconds(180),
        Position = TimeSpan.FromSeconds(10),
        IsExplicit = false,
        Artwork = SharedArtwork,
        UpdatedAt = DateTimeOffset.Now
    };

    [Fact]
    public void PositionOnly_DoesNotTriggerPresentationChange()
    {
        var prev = Base;
        var curr = Base with { Position = TimeSpan.FromSeconds(12), UpdatedAt = DateTimeOffset.Now.AddSeconds(1) };
        Assert.False(MediaPresentation.HasPresentationChanged(prev, curr));
        Assert.True(MediaPresentation.IsPositionOnlyChange(prev, curr));
    }

    [Fact]
    public void TitleChange_TriggersPresentationChange()
    {
        var prev = Base;
        var curr = Base with { Title = "Song B" };
        Assert.True(MediaPresentation.HasPresentationChanged(prev, curr));
    }

    [Fact]
    public void ArtistChange_TriggersPresentationChange()
    {
        var prev = Base;
        var curr = Base with { Artist = "Artist Y" };
        Assert.True(MediaPresentation.HasPresentationChanged(prev, curr));
    }

    [Fact]
    public void PlaybackStateChange_TriggersPresentationChange()
    {
        var prev = Base;
        var curr = Base with { PlaybackState = MediaPlaybackState.Paused };
        Assert.True(MediaPresentation.HasPresentationChanged(prev, curr));
    }

    [Fact]
    public void DurationChange_TriggersPresentationChange()
    {
        var prev = Base;
        var curr = Base with { Duration = TimeSpan.FromSeconds(200) };
        Assert.True(MediaPresentation.HasPresentationChanged(prev, curr));
    }

    [Fact]
    public void ArtworkReferenceChange_TriggersPresentationChange()
    {
        var prev = Base;
        var curr = Base with { Artwork = new byte[] { 1, 2, 3 } }; // different instance, same bytes
        Assert.True(MediaPresentation.HasPresentationChanged(prev, curr));
    }

    [Fact]
    public void SameArtworkReference_DoesNotTrigger()
    {
        var art = new byte[] { 9, 9, 9 };
        var prev = Base with { Artwork = art };
        var curr = Base with { Artwork = art };
        Assert.False(MediaPresentation.HasPresentationChanged(prev, curr));
    }

    [Fact]
    public void SourceAppChange_TriggersPresentationChange()
    {
        var prev = Base;
        var curr = Base with { SourceAppId = "chrome.exe", SourceAppName = "Chrome" };
        Assert.True(MediaPresentation.HasPresentationChanged(prev, curr));
    }

    [Fact]
    public void CanSeekChange_TriggersPresentationChange()
    {
        var prev = Base;
        var curr = Base with { CanSeek = false };
        Assert.True(MediaPresentation.HasPresentationChanged(prev, curr));
    }
}
