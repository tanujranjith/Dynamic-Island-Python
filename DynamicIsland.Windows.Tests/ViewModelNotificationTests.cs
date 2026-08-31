using DynamicIsland.Windows.Infrastructure;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public class ViewModelNotificationTests
{
    [Fact]
    public void MediaPresentation_DoesNotContainStructural()
    {
        var structural = ViewModelNotificationGroups.StructuralProperties;
        foreach (var prop in ViewModelNotificationGroups.MediaPresentationProperties)
            Assert.DoesNotContain(prop, structural);
    }

    [Fact]
    public void Audio_DoesNotContainStructural()
    {
        var structural = ViewModelNotificationGroups.StructuralProperties;
        foreach (var prop in ViewModelNotificationGroups.AudioProperties)
            Assert.DoesNotContain(prop, structural);
    }

    [Fact]
    public void Battery_DoesNotContainStructural()
    {
        var structural = ViewModelNotificationGroups.StructuralProperties;
        foreach (var prop in ViewModelNotificationGroups.BatteryProperties)
            Assert.DoesNotContain(prop, structural);
    }

    [Fact]
    public void Theme_DoesNotContainStructural()
    {
        var structural = ViewModelNotificationGroups.StructuralProperties;
        foreach (var prop in ViewModelNotificationGroups.ThemeProperties)
            Assert.DoesNotContain(prop, structural);
    }

    [Fact]
    public void MediaPresentation_ContainsExpectedMediaKeys()
    {
        var media = ViewModelNotificationGroups.MediaPresentationProperties;
        Assert.Contains("MediaTitle", media);
        Assert.Contains("MediaArtist", media);
        Assert.Contains("IsPlaying", media);
        Assert.Contains("PlayPauseGlyph", media);
        Assert.Contains("MediaProgress", media);
        Assert.Contains("Artwork", media);
        Assert.Contains("HasArtwork", media);
        Assert.Contains("ShowCompactArt", media);
        Assert.Contains("PrimaryActivity", media);
        Assert.Contains("CompactPrimaryText", media);
    }

    [Fact]
    public void Audio_ContainsVolumeKeys()
    {
        var audio = ViewModelNotificationGroups.AudioProperties;
        Assert.Contains("VolumeText", audio);
        Assert.Contains("VolumeGlyph", audio);
        Assert.Contains("IsMuted", audio);
        Assert.Contains("IsAudioActive", audio);
    }

    [Fact]
    public void Q_ContainsChatExperienceKeys()
    {
        var q = ViewModelNotificationGroups.QProperties;
        Assert.Contains("QHeaderStatusText", q);
        Assert.Contains("QResponseDisplay", q);
        Assert.Contains("QHasPrompt", q);
        Assert.Contains("QCanStop", q);
        Assert.Contains("QCanCopyResponse", q);
        Assert.Contains("QCanRetry", q);
        Assert.Contains("QShowResponseActions", q);
        Assert.Contains("QShortcuts", q);
        Assert.Contains("QHasShortcuts", q);
    }

    [Fact]
    public void Battery_ContainsBatteryKeys()
    {
        var batt = ViewModelNotificationGroups.BatteryProperties;
        Assert.Contains("BatteryText", batt);
        Assert.Contains("BatteryGlyph", batt);
        Assert.Contains("IsCharging", batt);
        Assert.Contains("ShowBattery", batt);
    }

    [Fact]
    public void MediaProgressOnly_IsSmallSubset()
    {
        var progress = ViewModelNotificationGroups.MediaProgressOnlyProperties;
        Assert.Equal(4, progress.Count);
        Assert.Contains("MediaProgress", progress);
        Assert.Contains("MediaElapsedText", progress);
        // Progress-only must never contain structural
        foreach (var p in progress)
            Assert.DoesNotContain(p, ViewModelNotificationGroups.StructuralProperties);
    }

    [Fact]
    public void Structural_ContainsIsStatsStyleAndIsCompact()
    {
        Assert.Contains("IsStatsStyle", ViewModelNotificationGroups.StructuralProperties);
        Assert.Contains("IsAppleStyle", ViewModelNotificationGroups.StructuralProperties);
        Assert.Contains("IsCompact", ViewModelNotificationGroups.StructuralProperties);
        Assert.Contains("PinExpanded", ViewModelNotificationGroups.StructuralProperties);
    }

    // Window hardening: visual mode spurious notifications must not trigger layout
    private static bool ShouldApplyLayoutForVisualMode(bool oldIsStats, bool newIsStats, bool isExpanded, bool timerPanelOpen)
    {
        if (oldIsStats == newIsStats) return false;
        if (timerPanelOpen) return false;
        return isExpanded;
    }

    [Fact]
    public void VisualMode_SpuriousNotification_NoLayoutWhenModeUnchanged()
    {
        // Track stays Stats, notification arrives but mode didn't change -> no layout
        Assert.False(ShouldApplyLayoutForVisualMode(oldIsStats: true, newIsStats: true, isExpanded: true, timerPanelOpen: false));
        Assert.False(ShouldApplyLayoutForVisualMode(oldIsStats: false, newIsStats: false, isExpanded: true, timerPanelOpen: false));
    }

    [Fact]
    public void VisualMode_RealChange_AppliesLayoutOnlyWhenExpanded()
    {
        Assert.True(ShouldApplyLayoutForVisualMode(false, true, true, false));
        Assert.True(ShouldApplyLayoutForVisualMode(true, false, true, false));
        Assert.False(ShouldApplyLayoutForVisualMode(false, true, false, false)); // collapsed -> visual mode change handled via ApplyVisualMode elsewhere, not full layout
        Assert.False(ShouldApplyLayoutForVisualMode(false, true, true, true)); // timer panel open -> defer
    }

    [Fact]
    public void MediaProgressOnly_DoesNotIncludeTitle()
    {
        Assert.DoesNotContain("MediaTitle", ViewModelNotificationGroups.MediaProgressOnlyProperties);
        Assert.DoesNotContain("MediaArtist", ViewModelNotificationGroups.MediaProgressOnlyProperties);
    }
}
