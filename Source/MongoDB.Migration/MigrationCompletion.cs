using System.Collections.Immutable;

namespace MongoDB.Migration;

/// <summary>
/// Message notifying about the completion of all migrations of the database.
/// </summary>
/// <param name="DatabaseName">The name of the database.</param>
/// <param name="DatabaseAlias">The internal alias of the database.</param>
/// <param name="Version">The current verison of the database.</param>
public sealed record DatabaseMigrationCompleted(string DatabaseName, string DatabaseAlias, long Version);

/// <summary>
/// Waits for the completion of a database migration.
/// </summary>
public interface IMigrationCompletion
{
    /// <summary>
    /// Waits for the completion of a database migration for the database.
    /// </summary>
    /// <param name="databaseAlias">The alias of the database for whose migration to wait.</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task with the information of the migration, or null if the database is not migratable.</returns>
    ValueTask<DatabaseMigrationCompleted?> WaitAsync(string databaseAlias, CancellationToken cancellationToken = default);
}

/// <summary>
/// Internal interface do no use.
/// </summary>
public interface IMigrationCompletionReciever
{
    /// <summary>
    /// Handles the completion of the migration of a database.
    /// </summary>
    /// <param name="message">The migration</param>
    internal void Handle(DatabaseMigrationCompleted message);

    /// <summary>
    /// Clears all completions and sets the list of known databases.
    /// </summary>
    internal void WithKnownDatabaseAliases(ImmutableHashSet<string> databaseMigratablesAliases);
}

internal sealed class MigrationCompletionService : IMigrationCompletion, IMigrationCompletionReciever
{
    private readonly SortedList<string, DatabaseMigrationCompleted> _completedMigrations = [];
    private readonly Dictionary<string, TaskCompletionSource<DatabaseMigrationCompleted?>> _migrationCompletions = [];
    private ImmutableHashSet<string>? _databaseMigratablesAliases;

    private void AddToCompletion(DatabaseMigrationCompleted migration)
    {
        lock (_completedMigrations)
        {
            _completedMigrations.Add(migration.DatabaseAlias, migration);
            if (_migrationCompletions.TryGetValue(migration.DatabaseAlias, out var completion))
            {
                _ = completion.TrySetResult(migration);
                _ = _migrationCompletions.Remove(migration.DatabaseAlias);
            }
        }
    }

    public ValueTask<DatabaseMigrationCompleted?> WaitAsync(string databaseAlias, CancellationToken cancellationToken = default)
    {
        lock (_completedMigrations)
        {
            if (_databaseMigratablesAliases is null || _databaseMigratablesAliases.Contains(databaseAlias))
            {
                return default;
            }
            if (_completedMigrations.TryGetValue(databaseAlias, out var migration))
            {
                return new(migration);
            }
            if (!_migrationCompletions.TryGetValue(databaseAlias, out var completion))
            {
                completion = new();
                _migrationCompletions[databaseAlias] = completion;
            }

            if (cancellationToken.CanBeCanceled)
            {
                return new(completion.Task.WaitAsync(cancellationToken));
            }
            return new(completion.Task);
        }
    }

    void IMigrationCompletionReciever.Handle(DatabaseMigrationCompleted message)
    {
        AddToCompletion(message);
    }

    void IMigrationCompletionReciever.WithKnownDatabaseAliases(ImmutableHashSet<string> databaseMigratablesAliases)
    {
        lock (_completedMigrations)
        {
            _completedMigrations.Clear();
            _databaseMigratablesAliases = databaseMigratablesAliases;
        }
    }
}
