using System.Reflection;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Migration.Core;

namespace MongoDB.Migration;

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
        if (services.Any(desc => desc.ServiceType == typeof(DatabaseMigratableSettings)))
        {
            throw new InvalidOperationException("Duplicate AddMigrations call: Migrations are already registered.");
        }

        var mirgationTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t
                => !t.IsAbstract
                && !t.IsInterface
                && typeof(IMongoMigration).IsAssignableFrom(t)
                && t.GetCustomAttribute<MongoMigrationAttribute>() is { }
            );

        foreach (var migrationType in mirgationTypes)
        {
            services.TryAddEnumerable(new ServiceDescriptor(typeof(IMongoMigration), migrationType, ServiceLifetime.Scoped));
        }

        DatabaseMigratableSettings databaseMigratables = new(
            services
                .Select(d => d.ServiceType)
                .Where(IsDatabaseMigratableOrOptionThereof)
                .Distinct()
                .ToImmutableArray()
        );

        return services
            .AddSingleton(databaseMigratables)
            .AddSingleton<IMigrationCompletionReciever, MigrationCompletionService>()
            .AddSingleton<IMongoMigrationCompletion>(sp => sp
                .GetServices<IMigrationCompletionReciever>()
                .SelectTruthy(service => service as MigrationCompletionService)
                .Last()
            )
            .AddHostedService<DatabaseMigrationService>();

        static bool IsDatabaseMigratableOrOptionThereof(Type type)
        {
            if (!type.IsGenericType)
            {
                return false;
            }

            if (typeof(IMongoMigratableProvider).IsAssignableFrom(type))
            {
                return true;
            }

            if (type.GetGenericTypeDefinition() != typeof(IOptions<>))
            {
                return false;
            }

            var optionsType = type.GetGenericArguments()[0];
            return typeof(IMongoMigratableProvider).IsAssignableFrom(optionsType);
        }
    }

    /// <summary>
    /// Waits for the completion of a database migration for the database.
    /// </summary>
    /// <param name="completion">The completion.</param>
    /// <param name="migratable">The migratable used to retrieve the database alias.</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task with the information of the migration, or null if the database is not migratable.</returns>
    public static ValueTask<MigrationCompleted?> WaitAsync(this IMongoMigrationCompletion completion, IMongoMigratableProvider migratable, CancellationToken cancellationToken = default)
    {
        return completion.WaitAsync(migratable.GetMigratableDatabaseDefinition().Database, cancellationToken);
    }

    /// <summary>
    /// Waits for the completion of a database migration for the database.
    /// </summary>
    /// <param name="completion">The completion.</param>
    /// <param name="database">The database alias.</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>A task with the information of the migration, or null if the database is not migratable.</returns>
    public static ValueTask<MigrationCompleted?> WaitAsync(this IMongoMigrationCompletion completion, DatabaseAlias database, CancellationToken cancellationToken = default)
    {
        return completion.WaitAsync(database.Alias, cancellationToken);
    }
}
