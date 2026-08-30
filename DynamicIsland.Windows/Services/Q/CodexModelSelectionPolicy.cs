namespace DynamicIsland.Windows.Services.Q;

internal static class CodexModelSelectionPolicy
{
    public static IReadOnlyList<string> EffortOptions(CodexModel? model) =>
        model is { SupportedReasoningEfforts.Count: > 0 }
            ? ["auto", .. model.SupportedReasoningEfforts]
            : ["auto", "minimal", "low", "medium", "high", "xhigh", "max", "ultra"];

    public static string NormalizeEffort(CodexModel? model, string? selectedEffort)
    {
        var current = string.IsNullOrWhiteSpace(selectedEffort) ? "auto" : selectedEffort.Trim().ToLowerInvariant();
        if (model is null || model.SupportedReasoningEfforts.Count == 0 || current == "auto" ||
            model.SupportedReasoningEfforts.Contains(current, StringComparer.OrdinalIgnoreCase))
            return current;

        return model.SupportedReasoningEfforts.FirstOrDefault(effort =>
            string.Equals(effort, "low", StringComparison.OrdinalIgnoreCase))
            ?? model.DefaultReasoningEffort
            ?? "auto";
    }
}
