using System.Text.Json;

namespace SelectiveVpnRouter.Core.BrowserRouting;

public sealed partial class BrowserRoutingIpcDispatcher
{
    private static readonly HashSet<string> WriteRevisionFields = new(["expectedRevision"], StringComparer.Ordinal);
    private static readonly HashSet<string> UpsertParamsFields = new(["expectedRevision", "rule"], StringComparer.Ordinal);
    private static readonly HashSet<string> DeleteParamsFields = new(["expectedRevision", "id"], StringComparer.Ordinal);
    private static readonly HashSet<string> ResetParamsFields = new(["expectedRevision"], StringComparer.Ordinal);

    private Outcome UpsertRule(string id, JsonElement root)
    {
        const string method = BrowserRoutingIpcProtocol.Methods.UpsertRule;
        if (mutationStore is null)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.UnknownMethod);
        if (!TryReadWriteParams(id, root, UpsertParamsFields, out var p, out var badParams, method))
            return badParams;
        if (!TryReadExpectedRevision(p, out var expectedRevision, id, method, out var fail))
            return fail;
        if (!p.TryGetProperty("rule", out var ruleElement) || ruleElement.ValueKind != JsonValueKind.Object)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        var issues = new List<BrowserRoutingIssue>();
        var rule = BrowserRoutingValidator.TryParseRule(ruleElement, "/rule", issues);
        if (rule is null)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.ValidationFailed);
        try
        {
            var snapshot = mutationStore.UpsertRule(expectedRevision, rule);
            return MutationSuccess(id, method, snapshot);
        }
        catch (BrowserRoutingConcurrencyException ex)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.RevisionConflict,
                writeError: w => w.WriteNumber("currentRevision", ex.CurrentRevision));
        }
        catch (BrowserRoutingUnavailableException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.BrowserStateUnavailable);
        }
        catch (BrowserRoutingValidationException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.ValidationFailed);
        }
        catch (BrowserRoutingPersistenceException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.PersistenceFailed);
        }
    }

    private Outcome DeleteRule(string id, JsonElement root)
    {
        const string method = BrowserRoutingIpcProtocol.Methods.DeleteRule;
        if (mutationStore is null)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.UnknownMethod);
        if (!TryReadWriteParams(id, root, DeleteParamsFields, out var p, out var badParams, method))
            return badParams;
        if (!TryReadExpectedRevision(p, out var expectedRevision, id, method, out var fail))
            return fail;
        if (!p.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        var ruleId = idElement.GetString();
        if (string.IsNullOrEmpty(ruleId) || ruleId.Length > BrowserRoutingContract.MaxIdLength)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
        try
        {
            var snapshot = mutationStore.DeleteRule(expectedRevision, ruleId);
            return MutationSuccess(id, method, snapshot);
        }
        catch (BrowserRoutingConcurrencyException ex)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.RevisionConflict,
                writeError: w => w.WriteNumber("currentRevision", ex.CurrentRevision));
        }
        catch (BrowserRoutingRuleNotFoundException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.NotFound);
        }
        catch (BrowserRoutingUnavailableException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.BrowserStateUnavailable);
        }
        catch (BrowserRoutingValidationException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.ValidationFailed);
        }
        catch (BrowserRoutingPersistenceException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.PersistenceFailed);
        }
    }

    private Outcome ResetRules(string id, JsonElement root)
    {
        const string method = BrowserRoutingIpcProtocol.Methods.ResetRules;
        if (mutationStore is null)
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.UnknownMethod);
        if (!TryReadWriteParams(id, root, ResetParamsFields, out var p, out var badParams, method))
            return badParams;
        if (!TryReadExpectedRevision(p, out var expectedRevision, id, method, out var fail))
            return fail;
        try
        {
            var snapshot = mutationStore.ResetRules(expectedRevision);
            return MutationSuccess(id, method, snapshot);
        }
        catch (BrowserRoutingConcurrencyException ex)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.RevisionConflict,
                writeError: w => w.WriteNumber("currentRevision", ex.CurrentRevision));
        }
        catch (BrowserRoutingUnavailableException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.BrowserStateUnavailable);
        }
        catch (BrowserRoutingValidationException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.ValidationFailed);
        }
        catch (BrowserRoutingPersistenceException)
        {
            return Fail(id, method, BrowserRoutingIpcProtocol.Errors.PersistenceFailed);
        }
    }

    private static Outcome MutationSuccess(string id, string method, BrowserRoutingSnapshot snapshot)
    {
        var response = Success(id, writer =>
        {
            writer.WriteString("stateGeneration", snapshot.StateGeneration);
            writer.WriteNumber("revision", snapshot.Revision);
            writer.WriteString("defaultRoute", snapshot.State.DefaultRoute);
            writer.WriteNumber("ruleCount", snapshot.RuleCount);
        });
        return new Outcome(response, method, "ok");
    }

    private static bool TryReadWriteParams(string id, JsonElement root, HashSet<string> allowed, out JsonElement p, out Outcome fail, string method)
    {
        p = default;
        fail = default;
        if (!root.TryGetProperty("params", out p) || p.ValueKind != JsonValueKind.Object || !UniqueFields(p, allowed, exact: true))
        {
            fail = Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
            return false;
        }
        return true;
    }

    private static bool TryReadExpectedRevision(JsonElement p, out long expectedRevision, string id, string method, out Outcome fail)
    {
        expectedRevision = 0;
        fail = default;
        if (!p.TryGetProperty("expectedRevision", out var rev) || !BrowserRoutingValidator.TryReadSafeInteger(rev, out expectedRevision))
        {
            fail = Fail(id, method, BrowserRoutingIpcProtocol.Errors.InvalidRequest);
            return false;
        }
        return true;
    }
}
