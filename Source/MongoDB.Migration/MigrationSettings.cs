using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace MongoDB.Migration;

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

    /// <summary>
    /// The specific version to which to migrate the database; otherwise migrates up to the latest version.
    /// </summary>
    public long? MigrateToVersion { get; init; }

    DatabaseMigrationSettings IOptions<DatabaseMigrationSettings>.Value => this;
}
