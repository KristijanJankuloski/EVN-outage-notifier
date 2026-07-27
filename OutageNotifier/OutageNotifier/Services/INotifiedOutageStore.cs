namespace OutageNotifier.Services;

public interface INotifiedOutageStore
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);

    Task<HashSet<string>> GetNotifiedIdsAsync(CancellationToken cancellationToken);

    Task MarkNotifiedAsync(IReadOnlyCollection<string> prekinIds, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken);
}
