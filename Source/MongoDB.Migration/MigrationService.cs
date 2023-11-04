using System.Reflection;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using MongoDB.Migration.Core;

namespace MongoDB.Migration;

/// <summary>
/// Background service executing migrations.
/// </summary>
/// <param name="databaseMigratables">The list of migrations.</param>
/// <param name="serviceProvider">The service provider.</param>
/// <param name="migrationCompletedPublisher">Collects the migration completions.</param>
/// <param name="clock">The system clock.</param>
/// <param name="loggerFactory">The logger factory.</param>
internal sealed class DatabaseMigrationService(AvailableMigrationsTypes databaseMigratables, IServiceProvider serviceProvider, IMigrationCompletionReciever migrationCompletedPublisher, ISystemClock? clock = null, ILoggerFactory? loggerFactory = null)
    : IHostedService, IDisposable
{
    private readonly ILogger<DatabaseMigrationService>? _logger = loggerFactory?.CreateLogger<DatabaseMigrationService>();
    private Task? _executeTask;
    private CancellationTokenSource? _executeCts;

    public async Task MigrateToVersionAsync(MongoMigrableDefinition settings, CancellationToken stoppingToken)
    {
        var databaseAlias = settings.Database.Alias;

        _logger?.LogInformation(
            "Begining migration of {Database}",
            settings.Database.Alias
        );

        long migratedVersion;
        await using (var scope = serviceProvider.CreateAsyncScope())
        {

            var migrations = scope.ServiceProvider.GetServices<IMongoMigration>()
                .SelectTruthy(ToMigrationOrDefault)
                .Where(migration => migration.Database == databaseAlias)
                .ToImmutableArray();

            DatabaseMigrationProcessor processor = new(settings, clock, loggerFactory?.CreateLogger<DatabaseMigrationProcessor>());
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
            .Select(m => m.GetMigratableDefinition())
            .DistinctBy(s => s.Database.Alias)
            .ToImmutableArray();
        migrationCompletedPublisher.WithKnownDatabaseAliases(migrationSettings.Select(m => m.Database.Alias).ToImmutableHashSet());
        await Task.WhenAll(
            migrationSettings.Select(s => MigrateToVersionAsync(s, stoppingToken))
        )
            .ConfigureAwait(false);
    }

    private static IEnumerable<IMongoMigratable> GetDatabaseMigratables(AvailableMigrationsTypes databaseMigratables, IServiceProvider serviceProvider)
    {
        return databaseMigratables.MigratableTypes
            .Select(serviceProvider.GetServices)
            .SelectMany(s => s)
            .SelectTruthy(CastServiceToDatabaseMigratable);

        static IMongoMigratable? CastServiceToDatabaseMigratable(object? service)
        {
            if (service is IMongoMigratable m)
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
            var result = (IMongoMigratable)(optionsAccessor.GetValue(service) ?? throw new InvalidOperationException("The type is no U: IOptions<T> where T: IDatabaseMigratable || U: IDatabaseMigratable, or failed to produce a value."));
            return result;
        }
    }

    private static MigrationDescriptor? ToMigrationOrDefault(IMongoMigration service)
    {
        var serviceType = service.GetType();
        if (serviceType.GetCustomAttribute<MongoMigrationAttribute>() is not { } migrationDefinition)
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
