using System.Security.Cryptography;
using System.Text.Json;
using DynamicIsland.Windows.Services.Q;
using Xunit;

namespace DynamicIsland.Windows.Tests;

public sealed class CodexRuntimeAndSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dynamic-island-codex-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ValidBundledRuntimeWinsOverInstalledAndPathCopies()
    {
        var baseDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var bundledDirectory = Directory.CreateDirectory(Path.Combine(baseDirectory, "codex")).FullName;
        var bundled = Path.Combine(bundledDirectory, "codex.exe");
        var codeModeHost = Path.Combine(bundledDirectory, "codex-code-mode-host.exe");
        File.WriteAllBytes(bundled, [1, 2, 3, 4]);
        File.WriteAllBytes(codeModeHost, [4, 3, 2, 1]);
        WriteManifest(bundledDirectory, "0.151.0", ("codex.exe", Hash(bundled)), ("codex-code-mode-host.exe", Hash(codeModeHost)));

        var installed = Path.Combine(_root, "local", "Programs", "OpenAI", "Codex", "bin");
        Directory.CreateDirectory(installed);
        File.WriteAllBytes(Path.Combine(installed, "codex.exe"), [5]);

        var result = new CodexRuntimeResolver(baseDirectory, Path.Combine(_root, "local"), string.Empty).Resolve();

        Assert.Equal(CodexRuntimeSource.Bundled, result.Source);
        Assert.Equal(bundled, result.ExecutablePath);
        Assert.True(result.IsValidated);
        Assert.Equal("0.151.0", result.Version);
    }

    [Fact]
    public void ModifiedBundledRuntimeIsRejectedInsteadOfFallingBack()
    {
        var baseDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var bundledDirectory = Directory.CreateDirectory(Path.Combine(baseDirectory, "codex")).FullName;
        var bundled = Path.Combine(bundledDirectory, "codex.exe");
        var codeModeHost = Path.Combine(bundledDirectory, "codex-code-mode-host.exe");
        File.WriteAllBytes(bundled, [9, 9, 9]);
        File.WriteAllBytes(codeModeHost, [1]);
        WriteManifest(bundledDirectory, "0.151.0", ("codex.exe", new string('0', 64)), ("codex-code-mode-host.exe", Hash(codeModeHost)));

        var error = Assert.Throws<CodexAppServerException>(() =>
            new CodexRuntimeResolver(baseDirectory, Path.Combine(_root, "local"), string.Empty).Resolve());

        Assert.Equal(CodexFailureKind.RuntimeInvalid, error.Kind);
        Assert.DoesNotContain("codex.exe", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BundledManifestCannotOmitAnExecutable()
    {
        var baseDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var bundledDirectory = Directory.CreateDirectory(Path.Combine(baseDirectory, "codex")).FullName;
        var bundled = Path.Combine(bundledDirectory, "codex.exe");
        File.WriteAllBytes(bundled, [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(bundledDirectory, "codex-code-mode-host.exe"), [4, 5, 6]);
        WriteManifest(bundledDirectory, "0.151.0", ("codex-code-mode-host.exe", new string('0', 64)));

        var error = Assert.Throws<CodexAppServerException>(() =>
            new CodexRuntimeResolver(baseDirectory, Path.Combine(_root, "local"), string.Empty).Resolve());

        Assert.Equal(CodexFailureKind.RuntimeInvalid, error.Kind);
    }

    [Fact]
    public void OfficialInstallIsPreferredToPath()
    {
        var baseDirectory = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var local = Path.Combine(_root, "local");
        var installedDirectory = Directory.CreateDirectory(Path.Combine(local, "Programs", "OpenAI", "Codex", "bin")).FullName;
        var installed = Path.Combine(installedDirectory, "codex.exe");
        File.WriteAllBytes(installed, [1]);
        var pathDirectory = Directory.CreateDirectory(Path.Combine(_root, "path")).FullName;
        File.WriteAllBytes(Path.Combine(pathDirectory, "codex.exe"), [2]);

        var result = new CodexRuntimeResolver(baseDirectory, local, pathDirectory).Resolve();

        Assert.Equal(CodexRuntimeSource.OfficialInstall, result.Source);
        Assert.Equal(installed, result.ExecutablePath);
    }

    [Theory]
    [InlineData("usage limit reached", CodexFailureKind.UsageLimit)]
    [InlineData("401 unauthorized", CodexFailureKind.Unauthorized)]
    [InlineData("model is unavailable", CodexFailureKind.ModelUnavailable)]
    [InlineData("network connection failed", CodexFailureKind.Network)]
    [InlineData("turn interrupted", CodexFailureKind.Cancelled)]
    public void ProtocolFailuresHaveActionableCategories(string message, CodexFailureKind expected) =>
        Assert.Equal(expected, CodexErrorClassifier.Classify(message));

    [Fact]
    public void ThreadLedgerOnlyRemovesOwnedIds()
    {
        var path = Path.Combine(_root, "ledger", "threads.json");
        var ledger = new CodexThreadLedger(path);
        ledger.Add("dynamic-island-thread");

        Assert.Equal(["dynamic-island-thread"], ledger.Snapshot());
        ledger.Remove("some-other-app-thread");
        Assert.Equal(["dynamic-island-thread"], ledger.Snapshot());
        ledger.Remove("dynamic-island-thread");
        Assert.Empty(ledger.Snapshot());
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void WriteManifest(string directory, string version, params (string Name, string Sha256)[] files)
    {
        var manifest = new { version, files = files.Select(file => new { name = file.Name, sha256 = file.Sha256 }) };
        File.WriteAllText(Path.Combine(directory, "codex-runtime.json"), JsonSerializer.Serialize(manifest));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
