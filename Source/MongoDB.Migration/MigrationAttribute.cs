using MongoDB.Migration.Core;

namespace MongoDB.Migration;

/// <summary>
/// Annotates a <see cref="IMongoMigration"/> with version and database information.
/// </summary>
/// <param name="database">The name of the database alias.</param>
/// <param name="downVersion">The version to which <see cref="IMongoMigration.DownAsync"/> and from which <see cref="IMongoMigration.UpAsync"/> migrates.</param>
/// <param name="upVersion">The version to which <see cref="IMongoMigration.UpAsync"/> and from which <see cref="IMongoMigration.DownAsync"/> migrates.</param>
/// <seealso cref="IMongoMigration"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MongoMigrationAttribute(string database, long downVersion, long upVersion) : Attribute
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
    /// The version to which <see cref="IMongoMigration.DownAsync"/> and from which <see cref="IMongoMigration.UpAsync"/> migrates.
    /// </summary>
    public long UpVersion => upVersion;
    /// <summary>
    /// The version to which <see cref="IMongoMigration.UpAsync"/> and from which <see cref="IMongoMigration.DownAsync"/> migrates.
    /// </summary>
    public long DownVersion => downVersion;
}
