using System.Text.Json;

namespace DynamicIsland.Windows.Services.Q;

internal static class CodexModelCatalogParser
{
    public static IReadOnlyList<CodexModel> Parse(JsonElement result)
    {
        var models = new List<CodexModel>();
        if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return models;

        foreach (var item in data.EnumerateArray())
        {
            var id = String(item, "id") ?? String(item, "model");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var modalities = item.TryGetProperty("inputModalities", out var modes) && modes.ValueKind == JsonValueKind.Array
                ? modes.EnumerateArray().Select(value => value.GetString()).ToArray()
                : ["text", "image"];
            var efforts = new List<string>();
            if (item.TryGetProperty("supportedReasoningEfforts", out var supported) && supported.ValueKind == JsonValueKind.Array)
                foreach (var effort in supported.EnumerateArray())
                {
                    var name = String(effort, "reasoningEffort");
                    if (!string.IsNullOrWhiteSpace(name)) efforts.Add(name.Trim().ToLowerInvariant());
                }

            models.Add(new CodexModel(
                id,
                String(item, "displayName") ?? id,
                modalities.Any(value => string.Equals(value, "image", StringComparison.OrdinalIgnoreCase)),
                item.TryGetProperty("isDefault", out var isDefault) && isDefault.ValueKind == JsonValueKind.True,
                efforts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                String(item, "defaultReasoningEffort")?.Trim().ToLowerInvariant()));
        }

        return models;
    }

    private static string? String(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;
}
