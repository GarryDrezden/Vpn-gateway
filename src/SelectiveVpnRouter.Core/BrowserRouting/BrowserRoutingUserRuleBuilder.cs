using System.Text.RegularExpressions;

namespace SelectiveVpnRouter.Core.BrowserRouting;

public static partial class BrowserRoutingUserRuleBuilder
{
    [GeneratedRegex("^[a-z0-9]{12,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedIdBody();

    public static string GenerateRuleId() => "rule-" + Guid.NewGuid().ToString("N")[..16];

    public static bool IsGeneratedRuleId(string id) =>
        id.StartsWith("rule-", StringComparison.Ordinal) && GeneratedIdBody().IsMatch(id.AsSpan(5));

    public sealed record BuildResult(bool Ok, BrowserRoutingRule? Rule, string? Message, string? Field);

    public static BuildResult TryBuild(
        string? existingId,
        string name,
        string hostInput,
        string matchType,
        string routeMode,
        bool enabled,
        string? notes)
    {
        string id = string.IsNullOrWhiteSpace(existingId) ? GenerateRuleId() : existingId.Trim();
        if (BrowserRoutingValidator.ValidateRule(
                new BrowserRoutingRule(id, "a", "example.com", BrowserRoutingContract.ExactHost,
                    BrowserRoutingContract.RouteDirect, true, BrowserRoutingContract.SourceUser, null),
                "/rule").Any(i => i.Path == "/rule/id"))
            return new BuildResult(false, null, "Некорректный идентификатор правила.", "id");

        if (existingId is null && !IsGeneratedRuleId(id))
            return new BuildResult(false, null, "Некорректный идентификатор правила.", "id");

        if (string.IsNullOrWhiteSpace(name))
            return new BuildResult(false, null, "Укажите название правила.", "name");

        if (!BrowserRoutingHostInput.TryNormalize(hostInput, out string? host, out string? hostMessage))
            return new BuildResult(false, null, hostMessage ?? "Укажите корректный домен.", "host");

        if (!BrowserRoutingContract.MatchTypes.Contains(matchType))
            return new BuildResult(false, null, "Выберите тип совпадения.", "matchType");

        if (routeMode is not (BrowserRoutingContract.RouteVpn or BrowserRoutingContract.RouteDirect))
            return new BuildResult(false, null, "Выберите маршрут.", "routeMode");

        string? trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        var candidate = new BrowserRoutingRule(
            id,
            name.Trim(),
            host!,
            matchType,
            routeMode,
            enabled,
            BrowserRoutingContract.SourceUser,
            trimmedNotes);

        var issues = BrowserRoutingValidator.ValidateRule(candidate, "/rule");
        if (issues.Count > 0)
            return new BuildResult(false, null, "Проверьте поля правила и попробуйте снова.", null);

        return new BuildResult(true, candidate, null, null);
    }
}

public static class BrowserRoutingHostInput
{
    public static bool TryNormalize(string? raw, out string? host, out string? userMessage)
    {
        host = null;
        userMessage = null;
        if (raw is null)
        {
            userMessage = "Укажите домен.";
            return false;
        }

        string text = raw.Trim();
        if (text.Length == 0)
        {
            userMessage = "Укажите домен.";
            return false;
        }

        if (text.EndsWith('.'))
            text = text[..^1];

        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? absolute) || string.IsNullOrEmpty(absolute.Host))
            {
                userMessage = "Укажите корректный домен.";
                return false;
            }

            text = absolute.Host;
        }
        else if (text.StartsWith("//", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate("http:" + text, UriKind.Absolute, out Uri? absolute) || string.IsNullOrEmpty(absolute.Host))
            {
                userMessage = "Укажите корректный домен.";
                return false;
            }

            text = absolute.Host;
        }

        if (!Uri.TryCreate("http://" + text + "/", UriKind.Absolute, out Uri? parsed))
        {
            userMessage = "Укажите корректный домен.";
            return false;
        }

        host = parsed.Host.ToLowerInvariant();
        if (!BrowserRoutingValidator.IsCanonicalHost(host))
        {
            userMessage = "Укажите корректный домен.";
            return false;
        }

        return true;
    }
}
