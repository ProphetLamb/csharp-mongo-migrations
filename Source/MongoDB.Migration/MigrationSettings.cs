using System.Collections.Immutable;
using MongoDB.Migration.Core;

namespace MongoDB.Migration;

/// <summary>
/// Annotates options describing a migratable database.
/// </summary>
public interface IMongoMigratable
{
    /// <summary>
    /// Creates the definition for the migratable MongoDB database.
    /// </summary>
    /// <returns>The <see cref="MongoMigrableDefinition"/>.</returns>
    MongoMigrableDefinition GetMigratableDefinition();
}

/// <summary>
/// The list of service types implementing <see cref="IMongoMigratable"/>.
/// </summary>
/// <param name="types">The list of service types implementing <see cref="IMongoMigratable"/>.</param>
internal sealed class AvailableMigrationsTypes(ImmutableArray<Type> types)
{
    /// <summary>
    /// The list of service types implementing <see cref="IMongoMigratable"/>.
    /// </summary>
    public ImmutableArray<Type> MigratableTypes => types;
}
