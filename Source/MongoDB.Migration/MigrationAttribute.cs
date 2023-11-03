using MongoDB.Driver;

namespace MongoDB.Migration;

/// <summary>
/// Annotates a <see cref="IMigration"/> with version and database information.
/// </summary>
/// <param name="database">The name of the database alias.</param>
/// <param name="downVersion">The version to which <see cref="IMigration.DownAsync"/> and from which <see cref="IMigration.UpAsync"/> migrates.</param>
/// <param name="upVersion">The version to which <see cref="IMigration.UpAsync"/> and from which <see cref="IMigration.DownAsync"/> migrates.</param>
/// <seealso cref="IMigration"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
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
