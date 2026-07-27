using Microsoft.Extensions.Logging;
using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public sealed class OutageNotifierRunner
{
    private readonly IOutageApiClient _apiClient;
    private readonly INotifiedOutageStore _store;
    private readonly IOutageMatcher _matcher;
    private readonly IEmailSender _emailSender;
    private readonly IReadOnlyList<MatchRule> _matchRules;
    private readonly ILogger<OutageNotifierRunner> _logger;

    public OutageNotifierRunner(
        IOutageApiClient apiClient,
        INotifiedOutageStore store,
        IOutageMatcher matcher,
        IEmailSender emailSender,
        IReadOnlyList<MatchRule> matchRules,
        ILogger<OutageNotifierRunner> logger)
    {
        _apiClient = apiClient;
        _store = store;
        _matcher = matcher;
        _emailSender = emailSender;
        _matchRules = matchRules;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _store.EnsureSchemaAsync(cancellationToken);

        var notifiedIds = await _store.GetNotifiedIdsAsync(cancellationToken);
        var outages = await _apiClient.GetOutagesAsync(cancellationToken);

        var newMatches = outages
            .Where(o => !notifiedIds.Contains(o.PrekinId))
            .Where(o => _matcher.IsMatch(o, _matchRules))
            .ToList();

        if (newMatches.Count == 0)
        {
            _logger.LogInformation("No new matching outages found. {TotalFetched} outages fetched.", outages.Count);
            return;
        }

        _logger.LogInformation("Found {MatchCount} new matching outages. Sending notification email.", newMatches.Count);
        await _emailSender.SendOutageNotificationAsync(newMatches, cancellationToken);

        await _store.MarkNotifiedAsync(
            newMatches.Select(o => o.PrekinId).ToList(),
            DateTimeOffset.UtcNow,
            cancellationToken);

        _logger.LogInformation("Notification sent and {MatchCount} outage(s) recorded.", newMatches.Count);
    }
}
