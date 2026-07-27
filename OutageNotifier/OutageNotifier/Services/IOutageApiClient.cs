using OutageNotifier.Models;

namespace OutageNotifier.Services;

public interface IOutageApiClient
{
    Task<IReadOnlyList<Outage>> GetOutagesAsync(CancellationToken cancellationToken);
}
