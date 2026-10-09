using System.Text.Json;

namespace SelectiveVpnRouter.Core.BrowserRouting;

public interface IBrowserRoutingControlTransport
{
    Task<IpcResponse> InvokeAsync(string method, object? payload, CancellationToken cancellationToken);
}

public sealed record BrowserRoutingAppLoadResult(
    bool Ok,
    BrowserRoutingControlSnapshot? Snapshot,
    string? UserMessage);

public sealed record BrowserRoutingAppMutationResult(
    bool Ok,
    BrowserRoutingControlSnapshot? Snapshot,
    string? UserMessage,
    string? ErrorCode,
    bool Conflict,
    bool Ambiguous,
    bool NotFound);

public sealed class BrowserRoutingAppSession(IBrowserRoutingControlTransport transport)
{
    private static readonly HashSet<string> WriteMethods =
    [
        IpcMethods.UpsertBrowserRule,
        IpcMethods.DeleteBrowserRule,
        IpcMethods.ResetBrowserRules,
    ];

    public async Task<BrowserRoutingAppLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            IpcResponse response = await transport.InvokeAsync(IpcMethods.GetBrowserRoutingSnapshot, null, cancellationToken)
                .ConfigureAwait(false);
            if (!response.Ok)
                return new BrowserRoutingAppLoadResult(false, null, BrowserRoutingRulesMessages.StateUnavailable);

            BrowserRoutingControlSnapshot? snapshot = DeserializeSnapshot(response.PayloadJson);
            if (snapshot is null)
                return new BrowserRoutingAppLoadResult(false, null, BrowserRoutingRulesMessages.StateUnavailable);

            if (!snapshot.Available)
                return new BrowserRoutingAppLoadResult(false, snapshot, BrowserRoutingRulesMessages.StateUnavailable);

            return new BrowserRoutingAppLoadResult(true, snapshot, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new BrowserRoutingAppLoadResult(false, null, BrowserRoutingRulesMessages.StateUnavailable);
        }
    }

    public Task<BrowserRoutingAppMutationResult> UpsertAsync(
        BrowserRoutingRule rule,
        long expectedRevision,
        CancellationToken cancellationToken) =>
        MutateAsync(
            IpcMethods.UpsertBrowserRule,
            new UpsertBrowserRuleRequest { ExpectedRevision = expectedRevision, Rule = rule },
            cancellationToken);

    public Task<BrowserRoutingAppMutationResult> DeleteAsync(
        string id,
        long expectedRevision,
        CancellationToken cancellationToken) =>
        MutateAsync(
            IpcMethods.DeleteBrowserRule,
            new DeleteBrowserRuleRequest { ExpectedRevision = expectedRevision, Id = id },
            cancellationToken);

    public Task<BrowserRoutingAppMutationResult> ResetAllAsync(
        long expectedRevision,
        CancellationToken cancellationToken) =>
        MutateAsync(
            IpcMethods.ResetBrowserRules,
            new ResetBrowserRulesRequest { ExpectedRevision = expectedRevision },
            cancellationToken);

    private async Task<BrowserRoutingAppMutationResult> MutateAsync(
        string method,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            IpcResponse response = await transport.InvokeAsync(method, payload, cancellationToken).ConfigureAwait(false);
            if (response.Ok)
            {
                var reloaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
                if (!reloaded.Ok || reloaded.Snapshot is null)
                {
                    return new BrowserRoutingAppMutationResult(
                        false,
                        reloaded.Snapshot,
                        BrowserRoutingRulesMessages.StateUnavailable,
                        "reload_failed",
                        Conflict: false,
                        Ambiguous: false,
                        NotFound: false);
                }

                return new BrowserRoutingAppMutationResult(
                    true,
                    reloaded.Snapshot,
                    null,
                    null,
                    Conflict: false,
                    Ambiguous: false,
                    NotFound: false);
            }

            return await HandleWriteFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (IpcTimeoutException) when (WriteMethods.Contains(method))
        {
            var reloaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return new BrowserRoutingAppMutationResult(
                false,
                reloaded.Snapshot,
                BrowserRoutingRulesMessages.Ambiguous,
                "ambiguous_transport",
                Conflict: false,
                Ambiguous: true,
                NotFound: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new BrowserRoutingAppMutationResult(
                false,
                null,
                BrowserRoutingRulesMessages.StateUnavailable,
                "transport_error",
                Conflict: false,
                Ambiguous: false,
                NotFound: false);
        }
    }

    private async Task<BrowserRoutingAppMutationResult> HandleWriteFailureAsync(
        IpcResponse response,
        CancellationToken cancellationToken)
    {
        string code = response.Error ?? BrowserRoutingIpcProtocol.Errors.InternalError;
        if (code == BrowserRoutingIpcProtocol.Errors.RevisionConflict)
        {
            var reloaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return new BrowserRoutingAppMutationResult(
                false,
                reloaded.Snapshot,
                BrowserRoutingRulesMessages.Conflict,
                code,
                Conflict: true,
                Ambiguous: false,
                NotFound: false);
        }

        if (code == BrowserRoutingIpcProtocol.Errors.NotFound)
        {
            var reloaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return new BrowserRoutingAppMutationResult(
                false,
                reloaded.Snapshot,
                BrowserRoutingRulesMessages.NotFound,
                code,
                Conflict: false,
                Ambiguous: false,
                NotFound: true);
        }

        if (code is BrowserRoutingIpcProtocol.Errors.ValidationFailed or BrowserRoutingIpcProtocol.Errors.InvalidRequest)
        {
            return new BrowserRoutingAppMutationResult(
                false,
                null,
                BrowserRoutingRulesMessages.Validation,
                code,
                Conflict: false,
                Ambiguous: false,
                NotFound: false);
        }

        return new BrowserRoutingAppMutationResult(
            false,
            null,
            BrowserRoutingRulesMessages.StateUnavailable,
            code,
            Conflict: false,
            Ambiguous: false,
            NotFound: false);
    }

    private static BrowserRoutingControlSnapshot? DeserializeSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        return JsonSerializer.Deserialize<BrowserRoutingControlSnapshot>(json, ConfigSerializer.JsonOptions);
    }
}
