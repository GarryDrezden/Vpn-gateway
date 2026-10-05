namespace SelectiveVpnRouter.Core.RoutingTrace;

public sealed class RoutingTraceProcessScope
{
    private sealed record TrackedProcess(
        string Path,
        DateTimeOffset StartUtc,
        int? ParentPid,
        bool InScope,
        RoutingTraceAttribution Attribution);

    private readonly RoutingTraceTarget _target;
    private readonly bool _followChildren;
    private readonly bool _includePackagedHelpers;
    private readonly HashSet<string> _targetPaths;
    private readonly HashSet<string> _associatedPaths;
    private readonly Dictionary<int, TrackedProcess> _byPid = new();

    public RoutingTraceProcessScope(RoutingTraceTarget target, bool followChildren, bool includePackagedHelpers)
    {
        _target = target;
        _followChildren = followChildren;
        _includePackagedHelpers = includePackagedHelpers;
        _targetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ApplicationRulesHelper.NormalizeExePath(target.PrimaryExecutablePath),
        };
        _associatedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in target.AssociatedExecutablePaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                _associatedPaths.Add(ApplicationRulesHelper.NormalizeExePath(path));
            }
        }
    }

    public void ObserveProcess(
        int pid,
        int? parentPid,
        string? path,
        DateTimeOffset startUtc,
        RoutingTraceProcessStartSource startSource = RoutingTraceProcessStartSource.Os)
    {
        if (pid <= 0 || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string normalized = ApplicationRulesHelper.NormalizeExePath(path);
        bool inScope = EvaluateInScope(normalized, parentPid);
        RoutingTraceAttribution attr = BuildAttribution(normalized, inScope, parentPid);

        if (_byPid.TryGetValue(pid, out TrackedProcess? existing))
        {
            if (startSource == RoutingTraceProcessStartSource.ConnectionTelemetry)
            {
                bool reusedInScope = inScope || existing.InScope;
                attr = BuildAttribution(normalized, reusedInScope, parentPid ?? existing.ParentPid);
                _byPid[pid] = existing with
                {
                    Path = normalized,
                    ParentPid = parentPid ?? existing.ParentPid,
                    InScope = reusedInScope,
                    Attribution = attr,
                };
                return;
            }

            if (existing.StartUtc != startUtc || !ApplicationRulesHelper.PathsEqual(existing.Path, normalized))
            {
                bool reusedInScope = inScope || (_followChildren && existing.InScope && parentPid == existing.ParentPid);
                attr = BuildAttribution(normalized, reusedInScope, parentPid);
                _byPid[pid] = new TrackedProcess(normalized, startUtc, parentPid, reusedInScope, attr);
                return;
            }

            if (inScope && !existing.InScope)
            {
                _byPid[pid] = existing with { InScope = true, Attribution = attr };
            }

            return;
        }

        if (startSource == RoutingTraceProcessStartSource.ConnectionTelemetry)
        {
            return;
        }

        _byPid[pid] = new TrackedProcess(normalized, startUtc, parentPid, inScope, attr);
    }


    public bool TryGetProcessStartUtcTicks(int pid, out long processStartUtcTicks)
    {
        if (_byPid.TryGetValue(pid, out TrackedProcess? tracked))
        {
            processStartUtcTicks = tracked.StartUtc.UtcTicks;
            return true;
        }

        processStartUtcTicks = 0;
        return false;
    }

    public bool ShouldIncludeObservation(int pid, string? path, out RoutingTraceAttribution attribution)
    {
        attribution = default!;
        if (!_byPid.TryGetValue(pid, out TrackedProcess tracked))
        {
            if (!string.IsNullOrWhiteSpace(path) && EvaluateInScope(ApplicationRulesHelper.NormalizeExePath(path), null))
            {
                attribution = BuildAttribution(ApplicationRulesHelper.NormalizeExePath(path), true, null);
                return true;
            }

            return false;
        }

        if (!string.IsNullOrWhiteSpace(path)
            && !ApplicationRulesHelper.PathsEqual(tracked.Path, ApplicationRulesHelper.NormalizeExePath(path)))
        {
            return false;
        }

        if (!tracked.InScope)
        {
            return false;
        }

        attribution = tracked.Attribution with { ProcessPath = path ?? tracked.Path };
        return true;
    }

    private bool EvaluateInScope(string normalizedPath, int? parentPid)
    {
        if (_targetPaths.Contains(normalizedPath))
        {
            return true;
        }

        if (_includePackagedHelpers && _associatedPaths.Contains(normalizedPath))
        {
            return true;
        }

        if (_followChildren && parentPid is int pp && _byPid.TryGetValue(pp, out TrackedProcess parent) && parent.InScope)
        {
            return true;
        }

        return false;
    }

    private RoutingTraceAttribution BuildAttribution(string normalizedPath, bool inScope, int? parentPid)
    {
        bool isTarget = _targetPaths.Contains(normalizedPath);
        bool isHelper = _associatedPaths.Contains(normalizedPath);
        bool isChild = inScope && !isTarget && !isHelper;

        return new RoutingTraceAttribution
        {
            ProcessPath = normalizedPath,
            InheritsTargetExpectedRoute = isTarget || isHelper || isChild,
            IsPackagedHelper = isHelper,
            IsChildProcess = isChild,
            LogicalRuleId = _target.RuleId,
            LogicalApplicationName = _target.DisplayName,
        };
    }
}
