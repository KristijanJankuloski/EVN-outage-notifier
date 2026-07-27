using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public sealed class OutageMatcher : IOutageMatcher
{
    public bool IsMatch(Outage outage, IReadOnlyList<MatchRule> rules)
    {
        foreach (var rule in rules)
        {
            if (RuleMatches(outage, rule))
                return true;
        }

        return false;
    }

    private static bool RuleMatches(Outage outage, MatchRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.NasMesto) && !Contains(outage.NasMesto, rule.NasMesto))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.Adresa) && !Contains(outage.Adresa, rule.Adresa))
            return false;

        return true;
    }

    private static bool Contains(string? haystack, string needle)
    {
        return haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
