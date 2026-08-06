namespace DynamicIsland.Windows.Models;

public sealed record PrivacySensorState(bool Camera, bool Microphone)
{
    public static readonly PrivacySensorState None = new(false, false);
    public bool Any => Camera || Microphone;
}
