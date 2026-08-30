namespace DynamicIsland.Windows.Services.Q;

public sealed class CodexAccountCoordinator
{
    private readonly CodexAppServerClient _client;
    private readonly LoggingService? _log;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _loginCts;
    private CodexAccountSnapshot _snapshot = new(CodexAccountState.Checking);

    public CodexAccountCoordinator(CodexAppServerClient client, LoggingService? log = null)
    {
        _client = client;
        _log = log;
    }

    public CodexAccountSnapshot Snapshot => _snapshot;
    public event Action<CodexAccountSnapshot>? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Publish(_snapshot with { State = CodexAccountState.Checking, Error = null, FailureKind = null });
            var account = await _client.ReadAccountAsync(false, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                Publish(new CodexAccountSnapshot(CodexAccountState.SignedOut, Runtime: _client.RuntimeInfo));
                return;
            }
            var models = await _client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
            var limit = await _client.ReadRateLimitAsync(cancellationToken).ConfigureAwait(false);
            var state = limit?.UsedPercent >= 100 ? CodexAccountState.LimitReached : CodexAccountState.Connected;
            Publish(new CodexAccountSnapshot(state, account, limit, _client.RuntimeInfo, models));
            _log?.Info($"Codex account refreshed: state={state}, models={models.Count}, runtime={_client.RuntimeInfo?.Source}, version={_client.RuntimeInfo?.Version ?? "unknown"}");
        }
        catch (CodexAppServerException ex)
        {
            var state = ex.Kind is CodexFailureKind.RuntimeMissing or CodexFailureKind.RuntimeInvalid or CodexFailureKind.RuntimeTooOld
                ? CodexAccountState.RuntimeUnavailable
                : ex.Kind is CodexFailureKind.SignedOut or CodexFailureKind.Unauthorized ? CodexAccountState.SignedOut : CodexAccountState.Error;
            Publish(new CodexAccountSnapshot(state, Runtime: _client.RuntimeInfo, FailureKind: ex.Kind, Error: ex.Message));
            _log?.Error($"Codex account refresh failed: category={ex.Kind}");
        }
        catch (Exception ex)
        {
            Publish(new CodexAccountSnapshot(CodexAccountState.Error, Runtime: _client.RuntimeInfo,
                FailureKind: CodexFailureKind.Unknown, Error: "Codex account status could not be refreshed."));
            _log?.Error("Codex account refresh failed: category=Unknown", ex);
        }
        finally { _operationGate.Release(); }
    }

    public async Task<CodexDeviceLogin> StartLoginAsync(CancellationToken cancellationToken = default)
    {
        CancelLogin();
        _loginCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var login = await _client.StartDeviceLoginAsync(_loginCts.Token).ConfigureAwait(false);
        Publish(_snapshot with { State = CodexAccountState.LoginPending, PendingLogin = login, Error = null });
        return login;
    }

    public async Task CompleteLoginAsync(CodexDeviceLogin login, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _loginCts?.Token ?? CancellationToken.None);
        try
        {
            await _client.WaitForLoginAsync(login.LoginId, linked.Token).ConfigureAwait(false);
            await RefreshAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _loginCts?.Dispose();
            _loginCts = null;
        }
    }

    public void CancelLogin()
    {
        var pending = _snapshot.PendingLogin;
        _loginCts?.Cancel();
        if (pending is not null) _ = _client.CancelLoginAsync(pending.LoginId, CancellationToken.None);
        _loginCts?.Dispose();
        _loginCts = null;
        if (_snapshot.State == CodexAccountState.LoginPending)
            Publish(_snapshot with { State = CodexAccountState.SignedOut, PendingLogin = null });
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        CancelLogin();
        await _client.LogoutAsync(cancellationToken).ConfigureAwait(false);
        Publish(new CodexAccountSnapshot(CodexAccountState.SignedOut, Runtime: _client.RuntimeInfo));
        _log?.Info("Codex account signed out");
    }

    private void Publish(CodexAccountSnapshot value)
    {
        _snapshot = value;
        Changed?.Invoke(value);
    }
}
