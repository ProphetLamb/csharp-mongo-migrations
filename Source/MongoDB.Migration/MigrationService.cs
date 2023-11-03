using System.Reflection;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace MongoDB.Migration;

/// <summary>
/// Maps the name of a MongoDB database to the alias for the database used in the codebase.
/// </summary>
/// <param name="Alias">The alias of the database used in the codebase.</param>
/// <param name="Name">The name of the MongoDB database.</param>
public sealed record DatabaseAlias(string Alias, string Name);

/// <summary>
/// Represents a version to which the database was migrated.
/// </summary>
/// <param name="Database">The name of the database alias.</param>
/// <param name="Version">The version of the database.</param>
/// <param name="Started">The time at which the migration started.</param>
/// <param name="Completed">The time at which the migration completed, if sucessful.</param>
[BsonIgnoreExtraElements]
public sealed record DatabaseVersion(string Database, long Version, DateTimeOffset Started, DateTimeOffset? Completed = null);

internal sealed record MigrationExecutionDescriptor(string Database, long UpVersion, long DownVersion, IMigration MigrationService, string? Description = null);

/// <summary>
/// Background service executing migrations.
/// </summary>
/// <param name="databaseMigratables">The list of migrations.</param>
/// <param name="serviceProvider">The service provider.</param>
/// <param name="migrationCompletedPublisher">Collects the migration completions.</param>
/// <param name="logger">The logger.</param>
/// <param name="clock">The clock.</param>
public sealed class DatabaseMigrationService(DatabaseMigratableSettings databaseMigratables, IServiceProvider serviceProvider, IMigrationCompletionReciever migrationCompletedPublisher, ILogger<DatabaseMigrationService>? logger = null)
    : IHostedService, IDisposable
{
    private Task? _executeTask;
    private CancellationTokenSource? _executeCts;

    private static async Task<(long Count, DatabaseVersion? First, DatabaseVersion? Current)> GetMigrationStateAsync(IMongoCollection<DatabaseVersion> collection, string databaseName, CancellationToken cancellationToken)
    {
        var migrationsCount = await collection
            .Find(migration => migration.Database == databaseName)
            .CountDocumentsAsync(cancellationToken)
            .ConfigureAwait(false);
        var firstMigration = await collection
            .Find(migration => migration.Database == databaseName)
            .SortBy(migration => migration.Version)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentMigration = await collection
            .Find(migration => migration.Database == databaseName)
            .SortByDescending(migration => migration.Version)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        return (migrationsCount, firstMigration, currentMigration);
    }

    private static MigrationExecutionDescriptor? ToMigrationOrDefault(IMigration service)
    {
        var serviceType = service.GetType();
        if (serviceType.GetCustomAttribute<MigrationAttribute>() is not { } migrationDefinition)
        {
            return null;
        }
        return new(
            migrationDefinition.Database,
            migrationDefinition.UpVersion,
            migrationDefinition.DownVersion,
            service,
            migrationDefinition.Description
        );
    }

    /// <summary>
    /// Migrates the database to the latest version.
    /// </summary>
    /// <param name="settings">The options specifying the available migations.</param>
    /// <param name="stoppingToken">Cancels the migration.</param>
    /// <returns>The task.</returns>
    public async Task MigrateToVersionAsync(DatabaseMigrationSettings settings, CancellationToken stoppingToken)
    {
        logger?.LogInformation(
            "Begining migration of {Database}",
            settings.Database.Alias
        );

        long migratedVersion;
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            migratedVersion = await MigrateToVersionScoped(scope, settings, stoppingToken).ConfigureAwait(false);
        }

        logger?.LogInformation(
            "Completed migration {Database}",
            settings.Database.Alias
        );

        migrationCompletedPublisher.Handle(
            new(
                settings.Database.Name,
                settings.Database.Alias,
                migratedVersion
            )
        );

        async Task<long> MigrateToVersionScoped(AsyncServiceScope scope, DatabaseMigrationSettings options, CancellationToken stoppingToken)
        {
            var databaseName = options.Database.Name;
            var databaseAlias = options.Database.Alias;

            var migrations = scope.ServiceProvider.GetServices<IMigration>()
                .SelectTruthy(ToMigrationOrDefault)
                .Where(migration => migration.Database == databaseAlias)
                .ToImmutableArray();

            logger?.LogInformation(
                "Found {MigrationCount} locally available migrations for {Database} from {LowestVersion} to {HighestVersion}",
                migrations.Length,
                databaseName,
                migrations.Select(m => m.DownVersion).Min(),
                migrations.Select(m => m.UpVersion).Max()
            );

            logger?.LogInformation("Determine database migration state");

            MongoClient client = new(options.ConnectionString);
            var database = client.GetDatabase(databaseName);
            var collection = database.GetCollection<DatabaseVersion>(options.MirgrationStateCollectionName);

            var (migrationsCount, firstMigration, currentMigration) = await GetMigrationStateAsync(collection, databaseAlias, stoppingToken).ConfigureAwait(false);

            logger?.LogInformation(
                "Found {MigrationCount} applied migrations for {Database} at version {CurrentVersion} from {LowestVersion} to {HighestVersion}",
                migrationsCount,
                databaseName,
                currentMigration?.Version,
                firstMigration?.Version,
                currentMigration?.Version
            );
            logger?.LogInformation(
                "Determine all required migation"
            );

            var (downgrade, requiredMigrations) = (currentMigration?.Version, settings.MigrateToVersion) switch
            {
                (var currentVersion, null) => (
                    false,
                    GetRequiredMigrations(migrations, currentVersion).ToImmutableArray()
                ),
                (null, var targetVersion) => (
                    false,
                    GetRequiredMigrations(migrations.Where(m => m.UpVersion <= targetVersion).ToImmutableArray(), null).ToImmutableArray()
                ),
                (var currentVersion, var targetVersion) => currentVersion < targetVersion
                    ? (false, GetRequiredMigrations(migrations.Where(m => m.UpVersion <= targetVersion && m.DownVersion >= currentVersion).ToImmutableArray(), currentVersion).ToImmutableArray())
                    : (true, GetRequiredMigrations(migrations.Where(m => m.DownVersion >= targetVersion && m.UpVersion <= currentVersion).ToImmutableArray(), targetVersion).Reverse().ToImmutableArray())
            };

            if (requiredMigrations.IsDefaultOrEmpty)
            {
                return currentMigration?.Version ?? 0;
            }

            if (downgrade)
            {
                return await MigrateDownToVersionAsync(requiredMigrations, stoppingToken).ConfigureAwait(false);
            }

            return await MigrateUpToVersionAsync(requiredMigrations, stoppingToken).ConfigureAwait(false);

            async Task<long> MigrateUpToVersionAsync(ImmutableArray<MigrationExecutionDescriptor> requiredMigrations, CancellationToken stoppingToken)
            {
                logger?.LogInformation(
                    "Migrating {Database} in {RequiredMigrationsCount} steps {RequiredMigrationsVersions}",
                    databaseName,
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

                    var startedTimestamp = DateTimeOffset.UtcNow;
                    DatabaseVersion startedVersion = new(databaseName, migration.UpVersion, startedTimestamp, null);
                    await collection.InsertOneAsync(startedVersion, null, stoppingToken).ConfigureAwait(false);

                    await migration.MigrationService.UpAsync(database, stoppingToken).ConfigureAwait(false);

                    var completedTimestamp = DateTimeOffset.UtcNow;
                    _ = await collection.UpdateOneAsync(
                        v => v.Version == startedVersion.Version,
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

            async Task<long> MigrateDownToVersionAsync(ImmutableArray<MigrationExecutionDescriptor> requiredMigrations, CancellationToken stoppingToken)
            {
                logger?.LogInformation(
                    "Migrating {Database} in {RequiredMigrationsCount} steps {RequiredMigrationsVersions}",
                    databaseName,
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

                    var startedTimestamp = DateTimeOffset.UtcNow;
                    DatabaseVersion startedVersion = new(databaseName, migration.UpVersion, startedTimestamp, null);
                    await collection.InsertOneAsync(startedVersion, null, stoppingToken).ConfigureAwait(false);

                    await migration.MigrationService.UpAsync(database, stoppingToken).ConfigureAwait(false);

                    var completedTimestamp = DateTimeOffset.UtcNow;
                    _ = await collection.UpdateOneAsync(
                        v => v.Version == startedVersion.Version,
                        Builders<DatabaseVersion>.Update.Set(d => d.Completed, completedTimestamp),
                        null,
                        stoppingToken
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
    }

    private static IEnumerable<MigrationExecutionDescriptor> GetRequiredMigrations(ImmutableArray<MigrationExecutionDescriptor> availableMigrations, long? currentVersion)
    {
        return MigrationGraph.CreateOrDefault(availableMigrations, currentVersion)?.GetMigrationTrace() ?? Enumerable.Empty<MigrationExecutionDescriptor>();
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var migrationSettings = GetDatabaseMigratables(databaseMigratables, serviceProvider)
            .Select(m => m.GetMigrationSettings())
            .DistinctBy(s => s.Database.Alias)
            .ToImmutableArray();
        migrationCompletedPublisher.WithKnownDatabaseAliases(migrationSettings.Select(m => m.Database.Alias).ToImmutableHashSet());
        await Task.WhenAll(
            migrationSettings.Select(s => MigrateToVersionAsync(s, stoppingToken))
        )
            .ConfigureAwait(false);
    }

    private static IEnumerable<IDatabaseMigratable> GetDatabaseMigratables(DatabaseMigratableSettings databaseMigratables, IServiceProvider serviceProvider)
    {
        return databaseMigratables.MigratableTypes
            .Select(serviceProvider.GetServices)
            .SelectMany(s => s)
            .SelectTruthy(CastServiceToDatabaseMigratable);

        static IDatabaseMigratable? CastServiceToDatabaseMigratable(object? service)
        {
            if (service is IDatabaseMigratable m)
            {
                return m;
            }
            var implType = service?.GetType();
            if (implType is null
                || !implType.IsGenericType
                || implType.GetGenericTypeDefinition() != typeof(IOptions<>))
            {
                return null;
            }
            var optionsAccessor = implType
                .GetProperty(nameof(IOptions<object>.Value), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                ?? throw new InvalidOperationException("The type is no U: IOptions<T> where T: IDatabaseMigratable || U: IDatabaseMigratable, or failed to produce a value.");
            var result = (IDatabaseMigratable)(optionsAccessor.GetValue(service) ?? throw new InvalidOperationException("The type is no U: IOptions<T> where T: IDatabaseMigratable || U: IDatabaseMigratable, or failed to produce a value."));
            return result;
        }
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _executeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _executeTask = ExecuteAsync(_executeCts.Token);

        if (_executeTask.IsCompleted)
        {
            return _executeTask;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executeTask is null)
        {
            return;
        }
        try
        {
            _executeCts!.Cancel();
        }
        finally
        {
            // Wait until the task completes or the stop token triggers
            var tcs = new TaskCompletionSource<object>();
            using var registration = cancellationToken.Register(s => ((TaskCompletionSource<object>)s!).SetCanceled(), tcs);
            // Do not await the _executeTask because cancelling it will throw an OperationCanceledException which we are explicitly ignoring
            _ = await Task.WhenAny(_executeTask, tcs.Task).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _executeCts?.Cancel();
    }
}
