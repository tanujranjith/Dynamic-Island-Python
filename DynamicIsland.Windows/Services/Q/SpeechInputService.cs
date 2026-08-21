using DynamicIsland.Q.Core;
using Windows.Media.SpeechRecognition;

namespace DynamicIsland.Windows.Services.Q;

public sealed class SpeechInputService(LoggingService log) : IQSpeechInputService, IDisposable
{
    private SpeechRecognizer? _recognizer;
    public bool IsAvailable => SpeechRecognizer.SystemSpeechLanguage is not null;

    public async Task<string?> DictateAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable) return null;
        try
        {
            _recognizer ??= new SpeechRecognizer();
            await _recognizer.CompileConstraintsAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _recognizer.RecognizeAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return result.Status == SpeechRecognitionResultStatus.Success ? result.Text : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log.Error("Q speech recognition failed", ex);
            return null;
        }
    }

    public void Dispose() => _recognizer?.Dispose();
}
