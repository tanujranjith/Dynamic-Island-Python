using DynamicIsland.Windows.Infrastructure;

namespace DynamicIsland.Windows.ViewModels;

/// <summary>Editable settings row for a Q shortcut.</summary>
public sealed class QShortcutItem(string name, string prompt, Action sync) : ObservableObject
{
    private string _name = name;
    private string _prompt = prompt;

    public string Name
    {
        get => _name;
        set
        {
            if (!SetProperty(ref _name, value ?? "")) return;
            sync();
        }
    }

    public string Prompt
    {
        get => _prompt;
        set
        {
            if (!SetProperty(ref _prompt, value ?? "")) return;
            sync();
        }
    }
}
