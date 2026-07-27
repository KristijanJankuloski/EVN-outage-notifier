namespace OutageNotifier.Services;

public interface INotifiedOutageStore
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);

    Task<HashSet<int>> GetNotifiedIdsAsync(CancellationToken cancellationToken);

    Task MarkNotifiedAsync(IReadOnlyCollection<int> prekinIds, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken);
}
