using System.Collections.Immutable;
using MongoDB.Migration.Core;

namespace MongoDB.Migration;

/// <summary>
/// Annotates options describing a migratable database.
/// </summary>
public interface IMongoMigratableProvider
{
    /// <summary>
    /// Creates the definition for the migratable MongoDB database.
    /// </summary>
    /// <returns>The <see cref="MongoMigrableDefinition"/>.</returns>
    MongoMigrableDefinition GetMigratableDatabaseDefinition();
}

/// <summary>
/// The list of service types implementing <see cref="IMongoMigratableProvider"/>.
/// </summary>
/// <param name="types">The list of service types implementing <see cref="IMongoMigratableProvider"/>.</param>
internal sealed class DatabaseMigratableSettings(ImmutableArray<Type> types)
{
    /// <summary>
    /// The list of service types implementing <see cref="IMongoMigratableProvider"/>.
    /// </summary>
    public ImmutableArray<Type> MigratableTypes => types;
}
