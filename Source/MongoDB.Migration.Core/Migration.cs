using MongoDB.Driver;

namespace MongoDB.Migration.Core;

/// <summary>
/// Migrates a database between two versions.
/// </summary>
public interface IMongoMigration
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

/// <summary>
/// Fully defined description of a migration, with a <see cref="IMongoMigration"/>.
/// </summary>
/// <param name="Database">The alias of the database used in the codebase.</param>
/// <param name="UpVersion">The version to which <see cref="IMongoMigration.DownAsync"/> and from which <see cref="IMongoMigration.UpAsync"/> migrates.</param>
/// <param name="DownVersion">The version to which <see cref="IMongoMigration.UpAsync"/> and from which <see cref="IMongoMigration.DownAsync"/> migrates.</param>
/// <param name="MigrationService">The service performing the migration.</param>
/// <param name="Description">The description of the migraitons service.</param>
public sealed record MigrationDescriptor(string Database, long UpVersion, long DownVersion, IMongoMigration MigrationService, string? Description = null);


/// <summary>
/// Defines a migratable database.
/// </summary>
/// <remarks>
/// Maps the <see cref="DatabaseAlias.Alias"/> to the actual <see cref="DatabaseAlias.Name"/>.
/// </remarks>
public sealed record MongoMigrableDefinition
{
    /// <summary>
    /// The name of the collection within the database containing the list of applies migrations.
    /// </summary>
    public required string MirgrationStateCollectionName { get; init; }

    /// <summary>
    /// The mapping from the <see cref="DatabaseAlias.Alias"/> to the actual <see cref="DatabaseAlias.Name"/>.
    /// </summary>
    public required DatabaseAlias Database { get; init; }

    /// <summary>
    /// The connection string used to connect to the database.
    /// </summary>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// The fixed version to which to migrate the database; otherwise migrates up to the latest version.
    /// </summary>
    public long? MigrateToFixedVersion { get; init; }
}

