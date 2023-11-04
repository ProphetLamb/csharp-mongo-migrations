using System.Collections.Immutable;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace MongoDB.Migration.Core;

/// <summary>
/// Executes migrations on the specified database.
/// </summary>
/// <param name="settings">The settings describing the database.</param>
/// <param name="clock">The system clock.</param>
/// <param name="logger">The logger.</param>
[CLSCompliant(false)]
public sealed class DatabaseMigrationProcessor(MongoMigrableDefinition settings, ISystemClock? clock = null, ILogger<DatabaseMigrationProcessor>? logger = null)
{
    private static async Task<(long Count, DatabaseVersion? First, DatabaseVersion? Current, ImmutableArray<long> IncomplteMigrationVersions)> GetMigrationStateAsync(IMongoCollection<DatabaseVersion> collection, string databaseName, CancellationToken cancellationToken)
    {
        var migrationsCount = await ValidMigrations()
            .CountDocumentsAsync(cancellationToken)
            .ConfigureAwait(false);
        var firstMigration = await ValidMigrations()
            .SortBy(m => m.Completed)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentMigration = await ValidMigrations()
            .SortByDescending(m => m.Completed)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        var incompleteMigrationsVersions = await collection.Find(m => m.Database == databaseName && m.Completed == null).Project(m => m.Version).ToListAsync(cancellationToken).ConfigureAwait(false);
        return (migrationsCount, firstMigration, currentMigration, incompleteMigrationsVersions.ToImmutableArray());

        IFindFluent<DatabaseVersion, DatabaseVersion> ValidMigrations() => collection
            .Find(m => m.Database == databaseName && m.Completed != null);
    }

    private static async Task EnsureCollectionPreparedAsync(IMongoCollection<DatabaseVersion> collection, CancellationToken cancellationToken)
    {
        var indicesCursor = await collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false);
        var indices = await indicesCursor.ToListAsync(cancellationToken).ConfigureAwait(false);

        var index = GetOrderByCompletionIndex();
        if (!indices.Any(i => i["name"] == index.Options.Name))
        {
            _ = await collection.Indexes.CreateOneAsync(index, null, cancellationToken).ConfigureAwait(false);
        }

        static CreateIndexModel<DatabaseVersion> GetOrderByCompletionIndex()
        {
            IndexKeysDefinitionBuilder<DatabaseVersion> builder = new();
            var indexKey = builder.Descending(m => m.Completed);
            CreateIndexModel<DatabaseVersion> model = new(indexKey, new()
            {
                Name = "OrderByCompletionIndex",
                Unique = true
            });
            return model;
        }
    }

    /// <summary>
    /// Executes the migration using the specified list of available <paramref name="migrations"/>.
    /// </summary>
    /// <param name="migrations">The list of available migrations.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    public async Task<long> MigrateToVersionAsync(ImmutableArray<MigrationDescriptor> migrations, CancellationToken cancellationToken = default)
    {
        var databaseName = settings.Database.Name;
        var databaseAlias = settings.Database.Alias;

        logger?.LogInformation(
            "Found {MigrationCount} locally available migrations for {Database} from {LowestVersion} to {HighestVersion}",
            migrations.Length,
            databaseAlias,
            migrations.Select(m => m.DownVersion).Min(),
            migrations.Select(m => m.UpVersion).Max()
        );

        logger?.LogInformation("Preparing migration collection {MirgationCollection}", settings.MirgrationStateCollectionName);

        MongoClient client = new(settings.ConnectionString);
        var database = client.GetDatabase(databaseName);
        var collection = database.GetCollection<DatabaseVersion>(settings.MirgrationStateCollectionName);

        await EnsureCollectionPreparedAsync(collection, cancellationToken).ConfigureAwait(false);

        var (migrationsCount, firstMigration, currentMigration, incompleteMigrationsVersions) = await GetMigrationStateAsync(collection, databaseAlias, cancellationToken).ConfigureAwait(false);

        logger?.LogInformation(
            "Found {MigrationCount} applied migrations for {Database} at version {CurrentVersion} from {LowestVersion} to {HighestVersion}",
            migrationsCount,
            databaseAlias,
            currentMigration?.Version,
            firstMigration?.Version,
            currentMigration?.Version
        );

        if (!incompleteMigrationsVersions.IsDefaultOrEmpty)
        {
            logger?.LogCritical(
                "Cannot apply migrations because of a corrupt database: Found {IncomplteMigrationsCount} incomplte migrations for {Database}: {IncomplteMigrationsList}.",
                incompleteMigrationsVersions.Length,
                databaseAlias,
                string.Join(", ", incompleteMigrationsVersions)
            );
            throw new InvalidOperationException(
                $"Cannot apply migrations because of a corrupt database: Found {incompleteMigrationsVersions.Length} incomplte migrations for {databaseAlias}: {string.Join(", ", incompleteMigrationsVersions)}."
            );
        }

        logger?.LogInformation(
            "Determine all required migations"
        );

        var (downgrade, requiredMigrations) = GetRequiredMigrations(migrations, currentMigration?.Version, settings.MigrateToFixedVersion);
        if (requiredMigrations.IsDefaultOrEmpty)
        {
            logger?.LogInformation(
                "No migrations required, {Database} is already in the desired version {CurrentVersion}",
                databaseAlias,
                currentMigration?.Version ?? 0
            );
            return currentMigration?.Version ?? 0;
        }

        if (downgrade)
        {
            return await MigrateDownToVersionAsync(requiredMigrations, cancellationToken).ConfigureAwait(false);
        }

        return await MigrateUpToVersionAsync(requiredMigrations, cancellationToken).ConfigureAwait(false);

        async Task<long> MigrateUpToVersionAsync(ImmutableArray<MigrationDescriptor> requiredMigrations, CancellationToken stoppingToken)
        {
            logger?.LogInformation(
                "Migrating {Database} in {RequiredMigrationsCount} steps {RequiredMigrationsVersions}",
                databaseAlias,
                requiredMigrations.Length,
                $"{requiredMigrations.First().DownVersion} -> {string.Join(" -> ", requiredMigrations.Select(m => m.UpVersion))}"
            );

            foreach (var migration in requiredMigrations)
            {
                logger?.LogDebug(
                    "Begining migration for {Database} from {DownVersion} to {UpVersion}: {MigrationDescription}",
                    migration.Database,
                    migration.DownVersion,
                    migration.UpVersion,
                    migration.Description
                );

                var startedTimestamp = clock?.UtcNow ?? DateTimeOffset.UtcNow;
                DatabaseVersion startedVersion = new()
                {
                    Database = databaseName,
                    Version = migration.UpVersion,
                    Direction = VersionDirection.Up,
                    Started = startedTimestamp
                };
                await collection.InsertOneAsync(startedVersion, null, stoppingToken).ConfigureAwait(false);

                await migration.MigrationService.UpAsync(database, stoppingToken).ConfigureAwait(false);

                var completedTimestamp = clock?.UtcNow ?? DateTimeOffset.UtcNow;
                _ = await collection.UpdateOneAsync(
                    v => v.Id == startedVersion.Id,
                    Builders<DatabaseVersion>.Update.Set(d => d.Completed, completedTimestamp),
                    null,
                    stoppingToken
                )
                    .ConfigureAwait(false);

                logger?.LogDebug(
                    "Completed migration for {Database} from {DownVersion} to {UpVersion}",
                    migration.Database,
                    migration.DownVersion,
                    migration.UpVersion
                );
            }
            return requiredMigrations.Last().UpVersion;
        }

        async Task<long> MigrateDownToVersionAsync(ImmutableArray<MigrationDescriptor> requiredMigrations, CancellationToken cancellationToken)
        {
            logger?.LogInformation(
                "Migrating {Database} in {RequiredMigrationsCount} steps {RequiredMigrationsVersions}",
                databaseAlias,
                requiredMigrations.Length,
                $"{requiredMigrations.First().UpVersion} -> {string.Join(" -> ", requiredMigrations.Select(m => m.DownVersion))}"
            );

            foreach (var migration in requiredMigrations)
            {
                logger?.LogDebug(
                    "Begining migration for {Database} from {UpVersion} to {DownVersion}: {MigrationDescription}",
                    migration.Database,
                    migration.UpVersion,
                    migration.DownVersion,
                    migration.Description
                );

                var startedTimestamp = clock?.UtcNow ?? DateTimeOffset.UtcNow;
                DatabaseVersion startedVersion = new()
                {
                    Database = databaseName,
                    Version = migration.DownVersion,
                    Direction = VersionDirection.Down,
                    Started = startedTimestamp
                };
                await collection.InsertOneAsync(startedVersion, null, cancellationToken).ConfigureAwait(false);
                await migration.MigrationService.UpAsync(database, cancellationToken).ConfigureAwait(false);

                var completedTimestamp = clock?.UtcNow ?? DateTimeOffset.UtcNow;
                _ = await collection.UpdateOneAsync(
                    v => v.Id == startedVersion.Id,
                    Builders<DatabaseVersion>.Update.Set(d => d.Completed, completedTimestamp),
                    null,
                    cancellationToken
                )
                    .ConfigureAwait(false);

                logger?.LogDebug(
                    "Completed migration for {Database} from {UpVersion} to {DownVersion}",
                    migration.Database,
                    migration.UpVersion,
                    migration.DownVersion
                );
            }
            return requiredMigrations.Last().UpVersion;
        }
    }

    private static (bool IsDowngrade, ImmutableArray<MigrationDescriptor> MigrationsInOrder) GetRequiredMigrations(ImmutableArray<MigrationDescriptor> migrations, long? currentVersion, long? targetVersion)
    {
        var (isDowngrade, graph) = (currentVersion, targetVersion) switch
        {
            (long c, long t) => c < t
                ? (
                    false,
                    MigrationGraph.CreateOrDefault(migrations, currentVersion, targetVersion)
                )
                : (
                    true,
                    MigrationGraph.CreateOrDefault(migrations, targetVersion, currentVersion, true)
                ),
            _ => (
                false,
                MigrationGraph.CreateOrDefault(migrations, currentVersion, targetVersion)
            ),
        };
        return (
            isDowngrade,
            (
                isDowngrade
                    ? graph?.GetMigrationTrace().Reverse().ToImmutableArray()
                    : graph?.GetMigrationTrace().ToImmutableArray()
            ) ?? ImmutableArray<MigrationDescriptor>.Empty
        );
    }

}
