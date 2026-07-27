using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public interface IOutageMatcher
{
    bool IsMatch(Outage outage, IReadOnlyList<MatchRule> rules);
}
