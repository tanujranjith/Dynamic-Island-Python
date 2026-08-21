using DynamicIsland.Windows.Models;

namespace DynamicIsland.Windows.Infrastructure;

public static class MediaPresentation
{
    public static bool HasPresentationChanged(MediaInfo previous, MediaInfo current) =>
        !string.Equals(previous.Title, current.Title, StringComparison.Ordinal) ||
        !string.Equals(previous.Artist, current.Artist, StringComparison.Ordinal) ||
        !string.Equals(previous.Album, current.Album, StringComparison.Ordinal) ||
        !string.Equals(previous.SourceAppId, current.SourceAppId, StringComparison.Ordinal) ||
        !string.Equals(previous.SourceAppName, current.SourceAppName, StringComparison.Ordinal) ||
        previous.PlaybackState != current.PlaybackState ||
        previous.CanPlayPause != current.CanPlayPause ||
        previous.CanPrevious != current.CanPrevious ||
        previous.CanNext != current.CanNext ||
        previous.CanSeek != current.CanSeek ||
        previous.Duration != current.Duration ||
        previous.IsExplicit != current.IsExplicit ||
        !ReferenceEquals(previous.Artwork, current.Artwork);

    public static bool IsPositionOnlyChange(MediaInfo previous, MediaInfo current) =>
        !HasPresentationChanged(previous, current) &&
        (previous.Position != current.Position || previous.UpdatedAt != current.UpdatedAt);
}
