namespace DynamicIsland.Windows.Models;

/// <summary>A user-defined one-click prompt shown in the Q chat.</summary>
public sealed class QShortcut
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
}
