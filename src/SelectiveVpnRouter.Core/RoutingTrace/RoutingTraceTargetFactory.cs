namespace SelectiveVpnRouter.Core.RoutingTrace;

public static class RoutingTraceTargetFactory
{
    public static RoutingTraceTarget Resolve(
        StartRoutingTraceRequest request,
        AppConfiguration config,
        IPackagedApplicationPathResolver? packagedResolver)
    {
        if (request.RuleId is Guid ruleId)
        {
            RoutingRule? rule = config.Rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is null)
            {
                throw new InvalidOperationException($"Rule {ruleId} not found.");
            }

            return FromRule(rule, packagedResolver);
        }

        if (string.IsNullOrWhiteSpace(request.ExecutablePath))
        {
            throw new InvalidOperationException("Rule or executable path required.");
        }

        string path = ApplicationRulesHelper.NormalizeExePath(request.ExecutablePath);
        RoutingRule? byPath = ApplicationRulesHelper.FindApplicationRuleByPath(config.Rules, path);
        if (byPath is not null)
        {
            return FromRule(byPath, packagedResolver);
        }

        return new RoutingTraceTarget
        {
            DisplayName = System.IO.Path.GetFileName(path),
            PrimaryExecutablePath = path,
            ExpectedRoute = RoutingTraceExpectedRoute.Vpn,
        };
    }

    public static RoutingTraceTarget FromRule(RoutingRule rule, IPackagedApplicationPathResolver? packagedResolver)
    {
        string path = ApplicationRulesHelper.NormalizeExePath(rule.Target);
        var associated = new List<string>();
        if (packagedResolver is not null && rule.Type == RuleType.Application)
        {
            PackagedRoutingTargetsForRule plan = PackagedRoutingTargetResolver.ResolveForRule(rule, packagedResolver);
            foreach (PackagedRoutingTarget target in plan.Targets)
            {
                if (target.Kind == PackagedRoutingTargetKind.AssociatedHelper
                    && target.Resolved
                    && !string.IsNullOrWhiteSpace(target.ExecutablePath))
                {
                    associated.Add(ApplicationRulesHelper.NormalizeExePath(target.ExecutablePath));
                }
            }
        }

        return new RoutingTraceTarget
        {
            DisplayName = rule.Name,
            PrimaryExecutablePath = path,
            RuleId = rule.Id,
            ExpectedRoute = MapExpectedRoute(rule.Mode),
            AssociatedExecutablePaths = associated,
        };
    }

    private static RoutingTraceExpectedRoute MapExpectedRoute(RouteMode mode) => mode switch
    {
        RouteMode.Vpn => RoutingTraceExpectedRoute.Vpn,
        RouteMode.Direct => RoutingTraceExpectedRoute.Direct,
        _ => RoutingTraceExpectedRoute.DefaultDirect,
    };
}
