using System.Text.Json;

namespace SelectiveVpnRouter.Core.BrowserRouting;

public sealed record BrowserRoutingControlSnapshot
{
    public bool Available { get; init; }
    public string? UnavailableReason { get; init; }
    public string? StateGeneration { get; init; }
    public long Revision { get; init; }
    public string DefaultRoute { get; init; } = BrowserRoutingContract.RouteDirect;
    public IReadOnlyList<BrowserRoutingRule> Rules { get; init; } = [];
}

public sealed record UpsertBrowserRuleRequest
{
    public long ExpectedRevision { get; init; }
    public required BrowserRoutingRule Rule { get; init; }
}

public sealed record DeleteBrowserRuleRequest
{
    public long ExpectedRevision { get; init; }
    public required string Id { get; init; }
}

public sealed record ResetBrowserRulesRequest
{
    public long ExpectedRevision { get; init; }
}

public sealed record BrowserRoutingMutationSummary
{
    public required string StateGeneration { get; init; }
    public long Revision { get; init; }
    public required string DefaultRoute { get; init; }
    public int RuleCount { get; init; }
}

public sealed record BrowserRoutingControlErrorDetails
{
    public long? CurrentRevision { get; init; }
}

public sealed class BrowserRoutingControlException(string code, object? details = null)
    : Exception(code)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

public static class BrowserRoutingControlIpc
{
    public static BrowserRoutingControlSnapshot ReadSnapshot(BrowserRoutingStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.Status == BrowserRoutingStoreStatus.NotLoaded)
            store.Load();

        var current = store.Current;
        if (current is null)
        {
            return new BrowserRoutingControlSnapshot
            {
                Available = false,
                UnavailableReason = store.UnavailableReason ?? BrowserRoutingUnavailableReason.NotLoaded,
            };
        }

        return new BrowserRoutingControlSnapshot
        {
            Available = true,
            StateGeneration = current.StateGeneration,
            Revision = current.Revision,
            DefaultRoute = current.State.DefaultRoute,
            Rules = current.State.Rules,
        };
    }

    public static BrowserRoutingMutationSummary Upsert(BrowserRoutingStateStore store, UpsertBrowserRuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRuleForWrite(request.Rule);
        try
        {
            var snapshot = store.UpsertRule(request.ExpectedRevision, request.Rule);
            return ToSummary(snapshot);
        }
        catch (BrowserRoutingConcurrencyException ex)
        {
            throw new BrowserRoutingControlException(
                BrowserRoutingIpcProtocol.Errors.RevisionConflict,
                new BrowserRoutingControlErrorDetails { CurrentRevision = ex.CurrentRevision });
        }
        catch (BrowserRoutingUnavailableException)
        {
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.BrowserStateUnavailable);
        }
        catch (BrowserRoutingValidationException)
        {
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.ValidationFailed);
        }
        catch (BrowserRoutingPersistenceException)
        {
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.PersistenceFailed);
        }
    }

    public static BrowserRoutingMutationSummary Delete(BrowserRoutingStateStore store, DeleteBrowserRuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Id) || request.Id.Length > BrowserRoutingContract.MaxIdLength)
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.InvalidRequest);

        try
        {
            var snapshot = store.DeleteRule(request.ExpectedRevision, request.Id);
            return ToSummary(snapshot);
        }
        catch (BrowserRoutingConcurrencyException ex)
        {
            throw new BrowserRoutingControlException(
                BrowserRoutingIpcProtocol.Errors.RevisionConflict,
                new BrowserRoutingControlErrorDetails { CurrentRevision = ex.CurrentRevision });
        }
        catch (BrowserRoutingRuleNotFoundException)
        {
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.NotFound);
        }
        catch (BrowserRoutingUnavailableException)
        {
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.BrowserStateUnavailable);
        }
        catch (BrowserRoutingPersistenceException)
        {
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.PersistenceFailed);
        }
    }

    public static BrowserRoutingMutationSummary ResetRules(BrowserRoutingStateStore store, ResetBrowserRulesRequest request)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var snapshot = store.ResetRules(request.ExpectedRevision);
            return ToSummary(snapshot);
        }
        catch (BrowserRoutingConcurrencyException ex)
        {
            throw new BrowserRoutingControlException(
                BrowserRoutingIpcProtocol.Errors.RevisionConflict,
                new BrowserRoutingControlErrorDetails { CurrentRevision = ex.CurrentRevision });
        }
        catch (BrowserRoutingUnavailableException)
        {
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.BrowserStateUnavailable);
        }
        catch (BrowserRoutingValidationException)
        {
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.ValidationFailed);
        }
        catch (BrowserRoutingPersistenceException)
        {
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.PersistenceFailed);
        }
    }

    public static UpsertBrowserRuleRequest ParseUpsert(string? json)
    {
        UpsertBrowserRuleRequest? req = JsonSerializer.Deserialize<UpsertBrowserRuleRequest>(json ?? "{}", ConfigSerializer.JsonOptions)
            ?? throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        if (req.Rule is null)
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        return req;
    }

    public static DeleteBrowserRuleRequest ParseDelete(string? json)
    {
        DeleteBrowserRuleRequest? req = JsonSerializer.Deserialize<DeleteBrowserRuleRequest>(json ?? "{}", ConfigSerializer.JsonOptions)
            ?? throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        if (string.IsNullOrEmpty(req.Id))
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        return req;
    }

    public static ResetBrowserRulesRequest ParseReset(string? json)
    {
        ResetBrowserRulesRequest? req = JsonSerializer.Deserialize<ResetBrowserRulesRequest>(json ?? "{}", ConfigSerializer.JsonOptions);
        return req ?? new ResetBrowserRulesRequest();
    }

    private static void ValidateRuleForWrite(BrowserRoutingRule rule)
    {
        var issues = BrowserRoutingValidator.ValidateRule(rule, "/rule");
        if (issues.Count > 0)
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.ValidationFailed);
        if (!string.Equals(rule.Source, BrowserRoutingContract.SourceUser, StringComparison.Ordinal))
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.ValidationFailed);
        if (rule.RouteMode is not (BrowserRoutingContract.RouteVpn or BrowserRoutingContract.RouteDirect))
            throw new BrowserRoutingControlException(BrowserRoutingIpcProtocol.Errors.ValidationFailed);
    }

    private static BrowserRoutingMutationSummary ToSummary(BrowserRoutingSnapshot snapshot) =>
        new()
        {
            StateGeneration = snapshot.StateGeneration,
            Revision = snapshot.Revision,
            DefaultRoute = snapshot.State.DefaultRoute,
            RuleCount = snapshot.RuleCount,
        };
}
