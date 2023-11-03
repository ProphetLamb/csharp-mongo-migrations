using System.Reflection;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

/// <summary>
/// Annotates a <see cref="IMigration"/> with version and database information.
/// </summary>
/// <param name="database">The name of the database alias.</param>
/// <param name="downVersion">The version to which <see cref="IMigration.DownAsync"/> and from which <see cref="IMigration.UpAsync"/> migrates.</param>
/// <param name="upVersion">The version to which <see cref="IMigration.UpAsync"/> and from which <see cref="IMigration.DownAsync"/> migrates.</param>
/// <seealso cref="IMigration"/>
public sealed class MigrationAttribute(string database, long downVersion, long upVersion) : Attribute
{
    /// <summary>
    /// The optional description fo the migraiton.
    /// </summary>
    public string? Description { get; set; }
    /// <summary>
    /// The name of the database alias.
    /// </summary>
    public string Database => database;
    /// <summary>
    /// The version to which <see cref="IMigration.DownAsync"/> and from which <see cref="IMigration.UpAsync"/> migrates.
    /// </summary>
    public long UpVersion => upVersion;
    /// <summary>
    /// The version to which <see cref="IMigration.UpAsync"/> and from which <see cref="IMigration.DownAsync"/> migrates.
    /// </summary>
    public long DownVersion => downVersion;
}

/// <summary>
/// Migrates a database between two versions. Requires annotation with <see cref="MigrationAttribute"/>.
/// </summary>
/// <seealso cref="MigrationAttribute"/>
public interface IMigration
{
    /// <summary>
    /// Migrates the database from the down version to the up version.
    /// </summary>
    /// <param name="database">The database to migrate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task.</returns>
    Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates the database from the up version to the down version.
    /// </summary>
    /// <param name="database">The database to migrate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The task.</returns>
    Task DownAsync(IMongoDatabase database, CancellationToken cancellationToken = default);
}

internal sealed record MigrationExecutionDescriptor(string Database, long UpVersion, long DownVersion, IMigration MigrationService, string? Description = null);

/// <summary>
/// Annotates options describing a migratable database.
/// </summary>
public interface IDatabaseMigratable
{
    /// <summary>
    /// Creates the migration settings for the current options instance.
    /// </summary>
    /// <returns>The migration settings.</returns>
    DatabaseMigrationSettings GetMigrationSettings();
}

/// <summary>
/// Describes a migratable database.
/// </summary>
/// <remarks>
/// Maps the <see cref="DatabaseAlias.Alias"/> used in <see cref="MigrationAttribute"/> to the actual <see cref="DatabaseAlias.Name"/>.
/// </remarks>
public sealed record DatabaseMigrationSettings : IOptions<DatabaseMigrationSettings>
{
    /// <summary>
    /// The name of the collection within the database containing the list of applies migrations.
    /// </summary>
    public required string MirgrationStateCollectionName { get; init; }

    /// <summary>
    /// The mapping from the <see cref="DatabaseAlias.Alias"/> used in <see cref="MigrationAttribute"/> to the actual <see cref="DatabaseAlias.Name"/>.
    /// </summary>
    public required DatabaseAlias Database { get; init; }

    /// <summary>
    /// The connection string used to connect to the database.
    /// </summary>
    public required string ConnectionString { get; init; }

    DatabaseMigrationSettings IOptions<DatabaseMigrationSettings>.Value => this;
}

/// <summary>
/// Message notifying about the completion of all migrations of the database.
/// </summary>
/// <param name="DatabaseName">The name of the database.</param>
/// <param name="DatabaseAlias">The internal alias of the database.</param>
/// <param name="Version">The current verison of the database.</param>
public sealed record DatabaseMigrationCompleted(string DatabaseName, string DatabaseAlias, long Version);

/// <summary>
/// The list of service types implementing <see cref="IDatabaseMigratable"/>.
/// </summary>
/// <param name="types">The list of service types implementing <see cref="IDatabaseMigratable"/>.</param>
public sealed class DatabaseMigratableSettings(ImmutableArray<Type> types)
{
    /// <summary>
    /// The list of service types implementing <see cref="IDatabaseMigratable"/>.
    /// </summary>
    public ImmutableArray<Type> MigratableTypes => types;
}

/// <summary>
/// Extension itegrating the MongoDB migrations with ASP.NET.
/// </summary>
public static class MigrationExtensions
{
    /// <summary>
    /// Adds configured migrations to the service collection.
    /// </summary>
    /// <param name="services">The service colleciton.</param>
    /// <returns>The service colleciton.</returns>
    public static IServiceCollection AddMigrations(this IServiceCollection services)
    {
        return services.AddMigrations(Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly());
    }

    /// <summary>
    /// Adds configured migrations to the service collection.
    /// </summary>
    /// <param name="services">The service colleciton.</param>
    /// <param name="typesInAssemblies">The list of types in the assemblies to inject migrations for.</param>
    /// <returns>The service colleciton.</returns>
    public static IServiceCollection AddMigrations(this IServiceCollection services, params Type[] typesInAssemblies)
    {
        var assemblies = typesInAssemblies
            .Select(t => t.Assembly)
            .Distinct()
            .ToArray();
        return services.AddMigrations(assemblies);
    }

    /// <summary>
    /// Adds configured migrations to the service collection.
    /// </summary>
    /// <param name="services">The service colleciton.</param>
    /// <param name="assemblies">The list of assemblies to inject migrations for.</param>
    /// <returns>The service colleciton.</returns>
    public static IServiceCollection AddMigrations(this IServiceCollection services, params Assembly[] assemblies)
    {
        var mirgationTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t
                => !t.IsAbstract
                && !t.IsInterface
                && t.IsAssignableTo(typeof(IMigration))
                && t.GetCustomAttribute<MigrationAttribute>() is { }
            );

        foreach (var migrationType in mirgationTypes)
        {
            services.TryAddEnumerable(new ServiceDescriptor(typeof(IMigration), migrationType, ServiceLifetime.Scoped));
        }

        DatabaseMigratableSettings databaseMigratables = new(
            services
                .Select(d => d.ServiceType)
                .Where(type =>
                {
                    if (!type.IsGenericType)
                    {
                        return false;
                    }

                    if (type.IsAssignableTo(typeof(IDatabaseMigratable)))
                    {
                        return true;
                    }

                    if (type.GetGenericTypeDefinition() != typeof(IOptions<>))
                    {
                        return false;
                    }

                    var optionsType = type.GetGenericArguments()[0];
                    return optionsType.IsAssignableTo(typeof(IDatabaseMigratable));
                })
                .Distinct()
                .ToImmutableArray()
        );

        return services
            .AddSingleton<IMigrationCompletionReciever, MigrationCompletionService>()
            .AddSingleton<IMigrationCompletion>(sp => sp
                .GetServices<IMigrationCompletionReciever>()
                .SelectTruthy(service => service as MigrationCompletionService)
                .Last()
            )
            .AddSingleton(databaseMigratables)
            .AddSingleton<DatabaseMigrationService>();
    }

    /// <summary>
    /// Waits for the completion of a database migration for the database.
    /// </summary>
    /// <param name="completion">The completion.</param>
    /// <param name="migratable">The migratable used to retrieve the database alias.</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task with the information of the migration, or null if the database is not migratable.</returns>
    public static ValueTask<DatabaseMigrationCompleted?> WaitAsync(this IMigrationCompletion completion, IDatabaseMigratable migratable, CancellationToken cancellationToken = default)
    {
        return completion.WaitAsync(migratable.GetMigrationSettings().Database, cancellationToken);
    }

    /// <summary>
    /// Waits for the completion of a database migration for the database.
    /// </summary>
    /// <param name="completion">The completion.</param>
    /// <param name="database">The database alias.</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task with the information of the migration, or null if the database is not migratable.</returns>
    public static ValueTask<DatabaseMigrationCompleted?> WaitAsync(this IMigrationCompletion completion, DatabaseAlias database, CancellationToken cancellationToken = default)
    {
        return completion.WaitAsync(database.Alias, cancellationToken);
    }
}

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
    public async Task UpToLatestAsync(DatabaseMigrationSettings settings, CancellationToken stoppingToken)
    {
        logger?.LogInformation(
            "Begining migration of {Database}",
            settings.Database.Alias
        );

        long migratedVersion;
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            migratedVersion = await MigrateToLatestScoped(scope, settings, stoppingToken).ConfigureAwait(false);
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

        async Task<long> MigrateToLatestScoped(AsyncServiceScope scope, DatabaseMigrationSettings options, CancellationToken stoppingToken)
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
            var requiredMigrations = GetRequiredMigrations(migrations, currentMigration?.Version).ToImmutableArray();
            if (requiredMigrations.IsDefaultOrEmpty)
            {
                return currentMigration?.Version ?? 0;
            }
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
            migrationSettings.Select(s => UpToLatestAsync(s, stoppingToken))
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

/// <summary>
/// Computes a continioues path/trace of migration from one version to another.
/// </summary>
/// <param name="orderedMigrations">
/// Available migrations in ascending order <see cref="MigrationExecutionDescriptor.DownVersion"/>.
/// The first migration is the start/current migration.
/// </param>
sealed file class MigrationGraph
{
    private readonly IReadOnlyDictionary<long, ImmutableArray<Node>> _migrationByDownVersion;
    private readonly IReadOnlyDictionary<long, ImmutableArray<Node>> _migrationByUpVersion;
    private readonly long _startVersion;
    private readonly long _endVersion;
    private readonly ImmutableArray<MigrationExecutionDescriptor> _orderedMigrations;

    public MigrationGraph(ImmutableArray<MigrationExecutionDescriptor> migrations, long startVersion, long endVersion)
    {
        _orderedMigrations = migrations;
        _migrationByDownVersion = migrations
            .ToImmutableMap(m => m.DownVersion, m => new Node(m));
        _migrationByUpVersion = migrations
            .ToImmutableMap(m => m.UpVersion, m => NodesByDown(m.DownVersion).First(other => ReferenceEquals(other.Migration, m)));
        _startVersion = startVersion;
        _endVersion = endVersion;
    }

    /// <summary>
    /// Creates a <see cref="MigrationGraph"/> from an arbitrary sequence of migrations, starting at <see cref="MigrationExecutionDescriptor.DownVersion"/> greater or equal to <paramref name="currentVersion"/> and ending with <see cref="MigrationExecutionDescriptor.UpVersion"/> less then or equal to <paramref name="targetVersion"/> if specified.
    /// </summary>
    /// <param name="migrations">The sequence of migrations.</param>
    /// <param name="currentVersion">The minimum respected <see cref="MigrationExecutionDescriptor.DownVersion"/>.</param>
    /// <param name="targetVersion">The maximum respected <see cref="MigrationExecutionDescriptor.UpVersion"/>.</param>
    /// <returns></returns>
    public static MigrationGraph? CreateOrDefault(IEnumerable<MigrationExecutionDescriptor> migrations, long? currentVersion, long? targetVersion = null)
    {
        SortedList<long, MigrationExecutionDescriptor> orderedMigrations = [];
        foreach (var migration in migrations)
        {
            if ((currentVersion is { } c && c > migration.DownVersion)
                || (targetVersion is { } t && t < migration.UpVersion))
            {
                continue;
            }
            orderedMigrations.Add(migration.DownVersion, migration);
        }
        if (orderedMigrations.Count == 0)
        {
            return null;
        }
        return new(orderedMigrations.Values.ToImmutableArray(), migrations.First().DownVersion, migrations.Max(m => m.UpVersion));
    }

    private ImmutableArray<Node> NodesByDown(long version)
    {
        return _migrationByDownVersion.TryGetValue(version, out var nodes) ? nodes : ImmutableArray<Node>.Empty;
    }

    private ImmutableArray<Node> NodesByUp(long version)
    {
        return _migrationByUpVersion.TryGetValue(version, out var nodes) ? nodes : ImmutableArray<Node>.Empty;
    }

    /// <summary>
    /// Dijkstra algorithm.
    /// Trace linked list <see cref="Node.Previous"/>
    /// Distance to start <see cref="Node.Distance"/>
    /// </summary>
    private void TraceDistance()
    {
        PriorityQueue<Node, nuint> queue = new(_orderedMigrations.Length);
        Apply(node =>
        {
            node.IsVisited = false;
            node.Previous = null;
            if (node.Migration.DownVersion == _startVersion)
            {
                node.Distance = 0;
                queue.Enqueue(node, 0);
            }
            else
            {
                node.Distance = nuint.MaxValue;
            }
        });

        while (queue.TryDequeue(out var root, out var distance))
        {
            root.IsVisited = true;
            var nextDistance = distance + 1;
            foreach (var node in NodesByDown(root.Migration.UpVersion))
            {
                if (node.Distance <= nextDistance)
                {
                    continue;
                }
                node.Distance = nextDistance;
                node.Previous = root;
                if (!node.IsVisited)
                {
                    queue.Enqueue(node, node.Distance);
                }
            }
        }
    }

    private void EnsureTracePlausible()
    {
        var errors = ValidateTrace().ToArray();
        if (errors.Length == 0)
        {
            return;
        }
        if (errors.Length == 1)
        {
            throw errors[0];
        }
        throw new AggregateException($"Invalid migration set: no path from {_startVersion} to {_endVersion} exists", errors);

        IEnumerable<Exception> ValidateTrace()
        {
            // validate that the start and end nodes are connected
            if (!NodesByUp(_endVersion).Any(node => node.Previous is not null))
            {
                yield return new InvalidOperationException($"Invalid migration set: No path to the target version ({_endVersion}) exists with the available mirgrations.");
            }
            // validate that the start and end nodes are connected
            if (!NodesByDown(_startVersion).Any(node => node.IsVisited))
            {
                yield return new InvalidOperationException($"Invalid migration set: No path from the current version ({_startVersion}) exists with the available mirgrations.");
            }
        }
    }

    public IEnumerable<MigrationExecutionDescriptor> GetMigrationTrace()
    {
        TraceDistance();
        EnsureTracePlausible();
        Stack<MigrationExecutionDescriptor> trace = [];
        var downVersion = _endVersion;
        while (downVersion > _startVersion)
        {
            var closestNode = NodesByUp(downVersion)
                .Where(node => node.IsVisited)
                .MinBy(node => node.Distance);
            if (closestNode is null)
            {
                break;
            }
            trace.Push(closestNode.Migration);
            downVersion = closestNode.Migration.DownVersion;
        }
        if (downVersion != _startVersion)
        {
            throw new InvalidOperationException($"No path between the current version ({_startVersion}) and the intermediary version ({downVersion}) exists.");
        }
        return trace;
    }

    private void Apply(Action<Node> apply)
    {
        foreach (var nodes in _migrationByDownVersion.Values)
        {
            foreach (var node in nodes)
            {
                apply(node);
            }
        }
    }


    private sealed class Node(MigrationExecutionDescriptor migration)
    {
        public MigrationExecutionDescriptor Migration => migration;
        public nuint Distance { get; set; } = nuint.MaxValue;
        public bool IsVisited { get; set; }
        public Node? Previous { get; set; }
    }
}
