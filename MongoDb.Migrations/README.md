![Banner](Images/Banner.png)

# MongoDB Migration

Implements a database migration system for `MongoDB.Driver`.

## Quick start

### Inject services

Inject `AddMigration` into your services.

```csharp
services
    .Configure<MyDatabaseSettings>(builder.Configuration.GetSection("Database:MyDatabaseSettings"))
    .AddMigrations()
```

### Update settings

Implement `IDatabaseMigratable` for your database settings.

```csharp
public sealed record MyDatabaseSettings : IOptions<MyDatabaseSettings>, IDatabaseMigratable
{
    public required string ConnectionString { get; init; }
    public required string DatabaseName { get; init; }

    MyDatabaseSettings IOptions<MyDatabaseSettings>.Value => this;

    public DatabaseMigrationSettings GetMigrationSettings()
    {
        return new()
        {
            ConnectionString = ConnectionString,
            Database = new("MyDatabase", DatabaseName),
            MirgrationStateCollectionName = "DATABASE_MIGRATIONS"
        };
    }
}
```

### Await migrations

Wait for migrations to complete using `IMigrationCompletion` before accessing the database. The call is fast when the migration is completed, or the database is not configured.

```csharp
sealed class MyRepository(IOptions<MyDatabaseSettings> databaseSettings, IMigrationCompletion migrationCompletion) {
    private readonly IMongoCollection<MyModel> _myCollection = new MongoClient(databaseSettings.ConnectionString)
        .GetDatabase(databaseSettings.DatabaseName)
        .GetCollection<MyModel>(databaseSettings.MyCollectionName);

    public async Task<MyModel> GetOrSet(MyModel insertModel) {
        _ = await migrationCompletion.WaitAsync("MyDatabase").ConfigureAwait(false);
        // ... do stuff
    }
}

```

### Implement migrations

Add `IMigration`s between version 0 and 1 and so on...

```csharp
[Migration("MyDatabase", 0, 1, Description = "Add composite index to MyCollection")]
public sealed class PoeNinjaAddCompositeIndexMigration(IOptions<MyDatabaseSettings> optionsAccessor) : IMigration
{
    public async Task DownAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
    }

    public async Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
    }
}
```
