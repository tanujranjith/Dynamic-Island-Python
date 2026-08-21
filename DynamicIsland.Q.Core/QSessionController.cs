namespace DynamicIsland.Q.Core;

public sealed class QSessionController(IQProviderRegistry providers) : IQSessionController
{
    private readonly object _gate = new();
    private CancellationTokenSource? _activeCts;
    private readonly List<QMessage> _history = [];
    private QSessionSnapshot _snapshot = new(QRunState.Idle, QMode.Ask, string.Empty, string.Empty, "Ready", null, null, "", "");

    public QSessionSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public event Action<QSessionSnapshot>? Changed;

    public Task BeginAsync(QMode mode, string providerId, string model, QScreenContext? context, CancellationToken cancellationToken = default)
    {
        Publish(_snapshot with
        {
            State = QRunState.Ready,
            Mode = mode,
            Prompt = string.Empty,
            Response = string.Empty,
            Status = context is null ? "Ready for a question" : $"Reading {context.WindowTitle}",
            Error = null,
            Context = context,
            ProviderId = providerId,
            Model = model
        });
        return Task.CompletedTask;
    }

    public async Task SubmitAsync(string prompt, QMode mode, string providerId, string model, string? credential, string? baseUrl,
        bool includeImage, Func<CancellationToken, Task<QScreenContext?>>? recapture = null,
        CancellationToken cancellationToken = default, int maxResponseTokens = 1200)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return;
        Cancel();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate) _activeCts = linked;
        var token = linked.Token;
        try
        {
            var provider = providers.Find(providerId);
            if (provider is null) throw new InvalidOperationException($"Provider '{providerId}' is not available.");

            var context = Snapshot.Context;
            if (recapture is not null)
            {
                Publish(Snapshot with { State = QRunState.Capturing, Prompt = prompt, Mode = mode, Error = null, Response = string.Empty, ProviderId = providerId, Model = model, Status = "Reading active window…" });
                context = await recapture(token).ConfigureAwait(false);
            }

            Publish(Snapshot with { State = QRunState.Thinking, Prompt = prompt, Mode = mode, Context = context, Response = string.Empty, Error = null, ProviderId = providerId, Model = model, Status = "Thinking…" });
            var request = new QRequest(mode, prompt, context, _history.ToArray(), model,
                includeImage && context?.HasImage == true && provider.Info.Capabilities.HasFlag(QProviderCapabilities.Images),
                Math.Clamp(maxResponseTokens, 128, 4096));
            var response = new System.Text.StringBuilder();
            await foreach (var item in provider.StreamAsync(request, credential, baseUrl, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                switch (item)
                {
                    case QStreamEvent.Started:
                        Publish(Snapshot with { State = QRunState.Streaming, Status = "Q is responding…" });
                        break;
                    case QStreamEvent.Text text:
                        response.Append(text.Value);
                        Publish(Snapshot with { State = QRunState.Streaming, Response = response.ToString(), Status = "Q is responding…" });
                        break;
                    case QStreamEvent.Failed failed:
                        throw failed.Exception ?? new InvalidOperationException(failed.Message);
                    case QStreamEvent.Completed:
                        break;
                }
            }

            var answer = response.ToString().Trim();
            if (answer.Length > 0)
            {
                _history.Add(new QMessage("user", prompt));
                _history.Add(new QMessage("assistant", answer));
                while (_history.Count > 8) _history.RemoveAt(0);
            }
            Publish(Snapshot with { State = QRunState.Complete, Response = answer, Status = "Complete", Error = null });
        }
        catch (OperationCanceledException)
        {
            Publish(Snapshot with { State = QRunState.Cancelled, Status = "Cancelled" });
        }
        catch (Exception ex)
        {
            Publish(Snapshot with { State = QRunState.Error, Status = "Q could not respond", Error = ex.Message });
        }
        finally
        {
            linked.Dispose();
            lock (_gate) if (ReferenceEquals(_activeCts, linked)) _activeCts = null;
        }
    }

    public void Cancel()
    {
        lock (_gate) _activeCts?.Cancel();
    }

    public void Clear()
    {
        Cancel();
        _history.Clear();
        Publish(new QSessionSnapshot(QRunState.Idle, QMode.Ask, string.Empty, string.Empty, "Ready", null, null, "", ""));
    }

    private void Publish(QSessionSnapshot snapshot)
    {
        lock (_gate) _snapshot = snapshot;
        Changed?.Invoke(snapshot);
    }

    public void Dispose() => Clear();
}
