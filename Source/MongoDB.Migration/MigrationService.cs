using System.Reflection;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;

namespace MongoDB.Migration;

/// <summary>
/// Maps the name of a MongoDB database to the alias for the database used in the codebase.
/// </summary>
/// <param name="Alias">The alias of the database used in the codebase.</param>
/// <param name="Name">The name of the MongoDB database.</param>
public sealed record DatabaseAlias(string Alias, string Name);

/// <summary>
/// The current state of the migration.
/// </summary>
internal enum VersionDirection
{
    None = 0,
    Up = 1,
    Down = 2
}

/// <summary>
/// Represents a version to which the database was migrated.
/// </summary>
/// <remarks>
/// A version is only valid when <see cref="Completed"/> is the time at which the migration completed.
/// </remarks>
internal sealed record DatabaseVersion
{
    /// <summary>
    /// The name of the database alias.
    /// </summary>
    public required string Database { get; init; }
    /// <summary>
    /// The target version of the migration
    /// </summary>
    public required long Version { get; init; }
    /// <summary>
    /// Whether the version was reached by up- or downgrading the database.
    /// </summary>
    [BsonRepresentation(BsonType.String)]
    public required VersionDirection Direction { get; init; }
    /// <summary>
    /// The timestamp at which the migration started.
    /// </summary>
    public required DateTimeOffset Started { get; init; }
    /// <summary>
    /// The timestamp at which the migration completed.
    /// </summary>
    public DateTimeOffset? Completed { get; set; }
    /// <summary>
    /// The Id of the object in the database, or default.
    /// </summary>
    [BsonId]
    public ObjectId Id { get; set; }
};

/// <summary>
/// Fully defined description of a migration, a combination of the <see cref="MigrationAttribute"/> and <see cref="IMigration"/>.
/// </summary>
/// <param name="Database">The alias of the database used in the codebase.</param>
/// <param name="UpVersion">The version to which <see cref="IMigration.DownAsync"/> and from which <see cref="IMigration.UpAsync"/> migrates.</param>
/// <param name="DownVersion">The version to which <see cref="IMigration.UpAsync"/> and from which <see cref="IMigration.DownAsync"/> migrates.</param>
/// <param name="MigrationService">The service performing the migration.</param>
/// <param name="Description">The description of the migraitons service.</param>
public sealed record MigrationExecutionDescriptor(string Database, long UpVersion, long DownVersion, IMigration MigrationService, string? Description = null);

/// <summary>
/// Background service executing migrations.
/// </summary>
/// <param name="databaseMigratables">The list of migrations.</param>
/// <param name="serviceProvider">The service provider.</param>
/// <param name="migrationCompletedPublisher">Collects the migration completions.</param>
/// <param name="_logger">The logger.</param>
internal sealed class DatabaseMigrationService(DatabaseMigratableSettings databaseMigratables, IServiceProvider serviceProvider, IMigrationCompletionReciever migrationCompletedPublisher, ISystemClock? clock = null, ILoggerFactory? loggerFactory = null)
    : IHostedService, IDisposable
{
    private readonly ILogger<DatabaseMigrationService>? _logger = loggerFactory?.CreateLogger<DatabaseMigrationService>();
    private Task? _executeTask;
    private CancellationTokenSource? _executeCts;

    public async Task MigrateToVersionAsync(DatabaseMigrationSettings settings, CancellationToken stoppingToken)
    {
        var databaseAlias = settings.Database.Alias;

        _logger?.LogInformation(
            "Begining migration of {Database}",
            settings.Database.Alias
        );

        long migratedVersion;
        await using (var scope = serviceProvider.CreateAsyncScope())
        {

            var migrations = scope.ServiceProvider.GetServices<IMigration>()
                .SelectTruthy(ToMigrationOrDefault)
                .Where(migration => migration.Database == databaseAlias)
                .ToImmutableArray();

            DatabaseMirationProcessor processor = new(settings, clock, loggerFactory?.CreateLogger<DatabaseMirationProcessor>());
            migratedVersion = await processor.MigrateToVersionAsync(migrations, stoppingToken).ConfigureAwait(false);
        }

        _logger?.LogInformation(
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

/// <summary>
/// Executes migrations on the specified database.
/// </summary>
/// <param name="settings">The settings describing the database.</param>
/// <param name="logger">The optional logger.</param>
public sealed class DatabaseMirationProcessor(DatabaseMigrationSettings settings, ISystemClock? clock = null, ILogger<DatabaseMirationProcessor>? logger = null)
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
    public async Task<long> MigrateToVersionAsync(ImmutableArray<MigrationExecutionDescriptor> migrations, CancellationToken cancellationToken = default)
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
                string.Join(", ", incompleteMigrationsVersions.Select(v => v.ToString()))
            );
            return currentMigration?.Version ?? 0;
        }

        logger?.LogInformation(
            "Determine all required migations"
        );

        var (downgrade, requiredMigrations) = (currentMigration?.Version, settings.MigrateToFixedVersion) switch
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
            return await MigrateDownToVersionAsync(requiredMigrations, cancellationToken).ConfigureAwait(false);
        }

        return await MigrateUpToVersionAsync(requiredMigrations, cancellationToken).ConfigureAwait(false);

        async Task<long> MigrateUpToVersionAsync(ImmutableArray<MigrationExecutionDescriptor> requiredMigrations, CancellationToken stoppingToken)
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

        async Task<long> MigrateDownToVersionAsync(ImmutableArray<MigrationExecutionDescriptor> requiredMigrations, CancellationToken cancellationToken)
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

    private static IEnumerable<MigrationExecutionDescriptor> GetRequiredMigrations(ImmutableArray<MigrationExecutionDescriptor> availableMigrations, long? currentVersion)
    {
        return MigrationGraph.CreateOrDefault(availableMigrations, currentVersion)?.GetMigrationTrace() ?? Enumerable.Empty<MigrationExecutionDescriptor>();
    }

}
