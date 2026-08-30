using System.Text.Json;

namespace DynamicIsland.Windows.Services.Q;

public enum CodexFailureKind
{
    RuntimeMissing,
    RuntimeInvalid,
    RuntimeTooOld,
    SignedOut,
    Unauthorized,
    UsageLimit,
    ModelUnavailable,
    Network,
    Protocol,
    ServerExited,
    Cancelled,
    Unknown
}

public sealed class CodexAppServerException : Exception
{
    public CodexAppServerException(CodexFailureKind kind, string message, string? method = null,
        int? protocolCode = null, Exception? innerException = null) : base(message, innerException)
    {
        Kind = kind;
        Method = method;
        ProtocolCode = protocolCode;
    }

    public CodexFailureKind Kind { get; }
    public string? Method { get; }
    public int? ProtocolCode { get; }
}

internal static class CodexErrorClassifier
{
    public static CodexAppServerException FromProtocol(string method, JsonElement error)
    {
        var raw = String(error, "message") ?? $"Codex request '{method}' failed.";
        int? code = error.TryGetProperty("code", out var codeValue) && codeValue.TryGetInt32(out var parsed) ? parsed : null;
        var kind = Classify(raw);
        return new CodexAppServerException(kind, UserMessage(kind, raw), method, code);
    }

    public static CodexFailureKind Classify(string? message)
    {
        var value = message?.ToLowerInvariant() ?? string.Empty;
        if (value.Contains("usagelimit") || value.Contains("usage limit") || value.Contains("rate limit") || value.Contains("quota")) return CodexFailureKind.UsageLimit;
        if (value.Contains("unauthorized") || value.Contains("401") || value.Contains("expired token")) return CodexFailureKind.Unauthorized;
        if (value.Contains("not signed in") || value.Contains("authentication required") || value.Contains("login required")) return CodexFailureKind.SignedOut;
        if (value.Contains("model") && (value.Contains("unavailable") || value.Contains("not found") || value.Contains("unsupported"))) return CodexFailureKind.ModelUnavailable;
        if (value.Contains("connection") || value.Contains("network") || value.Contains("dns") || value.Contains("httpconnectionfailed")) return CodexFailureKind.Network;
        if (value.Contains("interrupted") || value.Contains("cancel")) return CodexFailureKind.Cancelled;
        return CodexFailureKind.Protocol;
    }

    public static string UserMessage(CodexFailureKind kind, string? fallback = null) => kind switch
    {
        CodexFailureKind.RuntimeMissing => "Codex is not available. Use the bundled build or install the official Codex app.",
        CodexFailureKind.RuntimeInvalid => "The bundled Codex runtime failed verification. Re-download the test package.",
        CodexFailureKind.RuntimeTooOld => "This Codex version is too old. Update Codex or use the bundled test build.",
        CodexFailureKind.SignedOut or CodexFailureKind.Unauthorized => "Your ChatGPT/Codex session is signed out or expired. Sign in again.",
        CodexFailureKind.UsageLimit => "Your ChatGPT Codex usage limit has been reached. Check the reset time and try again later.",
        CodexFailureKind.ModelUnavailable => "That Codex model or reasoning effort is unavailable. Refresh models and choose another option.",
        CodexFailureKind.Network => "Codex could not reach OpenAI. Check your connection and retry.",
        CodexFailureKind.ServerExited => "The Codex service stopped unexpectedly. Retry to restart it.",
        CodexFailureKind.Cancelled => "The Codex request was cancelled.",
        _ => string.IsNullOrWhiteSpace(fallback) ? "Codex could not complete the request." : fallback
    };

    private static string? String(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : null;
}
