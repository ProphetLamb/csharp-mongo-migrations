
using System.Collections.Immutable;
using System.Diagnostics;
using MongoDB.Driver;
using MongoDB.Migration.Core;
using Xunit;

namespace MongoDB.Migration.Test;

public class Class1Test
{
    private static List<LoggingMigration> CreateDefaultMigrations(List<(int From, int To)> versionLog)
    {
        // optimal path: 00->20->40->50->60->90
        return [
            new(00, 10, versionLog),
            new(00, 20, versionLog),
            new(15, 50, versionLog),
            new(20, 40, versionLog),
            new(40, 50, versionLog),
            new(50, 60, versionLog),
            new(50, 80, versionLog),
            new(60, 90, versionLog),
            new(80, 90, versionLog),
        ];
    }

    private async Task WithDb(Func<MongoMigrableDefinition, IMongoDatabase, Task> asyncAction)
    {
        MongoMigrableDefinition settings = new()
        {
            ConnectionString = "mongodb://localhost:27017",
            Database = new("TestDatabase", $"TestDatabase{Environment.CurrentManagedThreadId}"),
            MirgrationStateCollectionName = "MIGRATION_COLLECTION"
        };

        MongoClient c = new(settings.ConnectionString);
        var db = c.GetDatabase(settings.Database.Name);
        try
        {
            await asyncAction(settings, db);
        }
        finally
        {
            await c.DropDatabaseAsync(settings.Database.Name);
        }
    }

    [Fact]
    public async Task Given_MigrationsToVersion90_UpgradesDatabaseToVersion90() => await WithDb(async (settings, db) =>
    {
        List<(int From, int To)> versionLog = new();
        var migrations = CreateDefaultMigrations(versionLog)
            .Select(m => m.CreateDescriptor())
            .ToImmutableArray();
        DatabaseMigrationProcessor p = new(settings);
        var res = await p.MigrateToVersionAsync(migrations);
        Debug.Assert(res == 90);

        var col = db.GetCollection<DatabaseVersion>(settings.MirgrationStateCollectionName);
        var currentMigration = await col
            .Find(m => m.Completed != null)
            .Sort(Builders<DatabaseVersion>.Sort.Descending("$natural"))
            .FirstAsync();
        Debug.Assert(currentMigration.Version == 90);
    });

    private sealed class LoggingMigration(int downVersion, int upVersion, List<(int From, int To)> versionLog) : IMongoMigration
    {
        public Task DownAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
        {
            versionLog.Add((upVersion, downVersion));
            return Task.CompletedTask;
        }

        public Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
        {
            versionLog.Add((downVersion, upVersion));
            return Task.CompletedTask;
        }

        public MigrationDescriptor CreateDescriptor()
        {
            return new("TestDatabase", upVersion, downVersion, this);
        }
    }
}
