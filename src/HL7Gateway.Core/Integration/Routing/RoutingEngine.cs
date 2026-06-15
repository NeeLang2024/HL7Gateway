using HL7Gateway.Core.Entities;

namespace HL7Gateway.Core.Integration.Routing;

public sealed class RoutingMatchResult
{
    public RoutingRule Rule { get; init; } = null!;
    public string Action { get; init; } = RoutingActions.LegacyDefault;
}

public class RoutingEngine
{
    public RoutingMatchResult? Match(Hl7Message message, IReadOnlyList<RoutingRule> rules)
    {
        if (rules.Count == 0)
            return null;

        foreach (var rule in rules.OrderBy(r => r.Priority).ThenBy(r => r.Id))
        {
            if (!rule.IsEnabled)
                continue;
            if (!Matches(rule, message))
                continue;

            return new RoutingMatchResult
            {
                Rule = rule,
                Action = string.IsNullOrWhiteSpace(rule.Action)
                    ? RoutingActions.ForwardAdt
                    : rule.Action.Trim()
            };
        }

        return null;
    }

    public object DescribeMatch(Hl7Message message, IReadOnlyList<RoutingRule> rules)
    {
        var hit = Match(message, rules);
        if (hit is null)
            return new { matched = false, message = "无匹配规则" };

        return new
        {
            matched = true,
            ruleId = hit.Rule.Id,
            ruleName = hit.Rule.Name,
            action = hit.Action,
            priority = hit.Rule.Priority
        };
    }

    private static bool Matches(RoutingRule rule, Hl7Message message)
    {
        if (!string.IsNullOrWhiteSpace(rule.MessageType)
            && !string.Equals(rule.MessageType, message.MessageType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.TriggerEvent)
            && !string.Equals(rule.TriggerEvent, message.TriggerEvent, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.SendingApp)
            && !string.Equals(rule.SendingApp, message.SendingApp, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.SendingFacility)
            && !string.Equals(rule.SendingFacility, message.SendingFacility, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.SourceIpPattern)
            && !IpPatternMatch(rule.SourceIpPattern, message.SourceIp))
            return false;

        return true;
    }

    private static bool IpPatternMatch(string pattern, string ip)
    {
        pattern = pattern.Trim();
        if (pattern == "*" || pattern == "*.*.*.*")
            return true;
        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return ip.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(pattern, ip, StringComparison.OrdinalIgnoreCase);
    }
}
