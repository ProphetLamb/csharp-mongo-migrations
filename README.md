![Banner](Images/Banner.png)

# MongoDB Migration

A database migration system for `MongoDB.Driver` using ASP.NET.

## Quick start

The following steps are required to implement migrations for your solution.

### Inject services

Inject `AddMigration` into your services.

-   If migraitons are not in the `Assembly.GetEntryAssembly`, then manually specify the assemblies in the parameters of `AddMigrations`.

```csharp
services
    .Configure<MyDatabaseSettings>(builder.Configuration.GetSection("Database:MyDatabaseSettings"))
    .AddMigrations()
```

### Update settings

Implement `IDatabaseMigratable` for your database settings.

-   Options must be configured before services are injected.
-   A `Database.Alias` must be unqiue: The name used to identify the database during runtime.
-   A `Database.Name` must be unqiue: The name of the actual MongoDB database.
-   The value `DatabaseMigrationSettings.Database.Alias` must be a unqiue constant.

```csharp
public sealed record MyDatabaseSettings : IOptions<MyDatabaseSettings>, IDatabaseMigratable
{
    public required string ConnectionString { get; init; }
    public required string DatabaseName { get; init; }
    public required string MyCollectionName { get; init; }

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

Wait for migrations to complete using `IMigrationCompletion` before accessing the database at any point.

-   The call is fast when the migration is completed, or the database is not configured.
-   `IMigrationCompletion.WaitAsync` may not ever be called in constructors.

```csharp
sealed class MyRepository(IOptions<MyDatabaseSettings> databaseSettings, IMigrationCompletion migrationCompletion) {
    private readonly IMongoCollection<MyModel> _myCollection = new MongoClient(databaseSettings.ConnectionString)
        .GetDatabase(databaseSettings.DatabaseName)
        .GetCollection<MyModel>(databaseSettings.MyCollectionName);

    public async Task<MyModel> GetOrSetAsync(MyModel insertModel, CancellationToken cancellationToken = default) {
        _ = await migrationCompletion.WaitAsync(databaseSettings.Value, cancellationToken).ConfigureAwait(false);
        // ... do stuff
    }
}

```

### Implement migrations

Add `IMigration`s between version 0 and 1 and so on.

-   The alias used for the `MigrationAttribute` mut match the name in `IDatabaseMigratable.GetMigrationSettings().Database.Alias`.

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

## ToDO

-   [x] Allow upgrading the database to the latest version.
-   [ ] Allow upgrading the database to a specific verison.
-   [ ] Allow downgrading the database to a specific verison.
-   [ ] Add test cases.
