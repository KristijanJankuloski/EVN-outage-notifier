using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using OutageNotifier.Configuration;

namespace OutageNotifier.Services;

public sealed class SqliteNotifiedOutageStore : INotifiedOutageStore
{
    private readonly string _connectionString;

    public SqliteNotifiedOutageStore(IOptions<DatabaseOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS NotifiedOutages (
                PrekinId TEXT PRIMARY KEY,
                NotifiedAtUtc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<HashSet<string>> GetNotifiedIdsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PrekinId FROM NotifiedOutages;";

        var ids = new HashSet<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    public async Task MarkNotifiedAsync(IReadOnlyCollection<string> prekinIds, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = connection.BeginTransaction();

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM NotifiedOutages WHERE NotifiedAtUtc < $staleCutoff;";

            var staleCutoffParam = deleteCommand.CreateParameter();
            staleCutoffParam.ParameterName = "$staleCutoff";
            staleCutoffParam.Value = notifiedAtUtc.AddMonths(-1).ToString("O");
            deleteCommand.Parameters.Add(staleCutoffParam);

            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var distinctPrekinIds = prekinIds.Distinct().ToList();
        if (distinctPrekinIds.Count > 0)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO NotifiedOutages (PrekinId, NotifiedAtUtc) VALUES ($prekinId, $notifiedAt);";

            var prekinIdParam = insertCommand.CreateParameter();
            prekinIdParam.ParameterName = "$prekinId";
            insertCommand.Parameters.Add(prekinIdParam);

            var notifiedAtParam = insertCommand.CreateParameter();
            notifiedAtParam.ParameterName = "$notifiedAt";
            notifiedAtParam.Value = notifiedAtUtc.ToString("O");
            insertCommand.Parameters.Add(notifiedAtParam);

            foreach (var id in distinctPrekinIds)
            {
                prekinIdParam.Value = id;
                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
