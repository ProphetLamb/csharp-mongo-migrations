using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoDB.Migration.Core;

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
