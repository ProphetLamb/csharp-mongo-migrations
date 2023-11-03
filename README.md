![Banner](Images/Banner.png)

# MongoDB Migrations

Simple database migrations for [`MongoDB.Driver`](https://github.com/mongodb/mongo-csharp-driver) using [`ASP.NET`](https://dotnet.microsoft.com/en-us/apps/aspnet) DI.

## Quick start

Install the [NuGet package](https://www.nuget.org/packages/csharp-mongodb-migrations/) & Implement the database migrations system in your solution using the following steps:

```bash
dotnet add package csharp-mongodb-migrations
```

1.  [Inject migrations service](#inject-migrations-service)
2.  [Update settings](#update-settings)
3.  [Await migrations](#await-migrations)
4.  [Implement migrations](#implement-migrations)

### Inject migrations service

Inject `AddMigrations` into your services.

-   If migrations are not in the `Assembly.GetEntryAssembly`, then manually specify the assemblies in the parameters of `AddMigrations`.
-   Call `AddMigrations` after configuring options.

```csharp
using MongoDB.Migration;

services
    .Configure<MyDatabaseSettings>(builder.Configuration.GetSection("Database:MyDatabaseSettings"))
    .AddMigrations()
```

### Update settings

Implement `IMongoMigratable` for your database settings.

-   Options must be configured before injecting services.
-   A `Database.Alias` must be unique: The name used to identify the database during runtime.
-   A `Database.Name` must be unique: The name of the actual MongoDB database.
-   Assign a unique constant value to `MigrationDescriptor.Database.Alias`.

```csharp
public sealed record MyDatabaseSettings : IOptions<MyDatabaseSettings>, IMongoMigratable
{
    public required string ConnectionString { get; init; }
    public required string DatabaseName { get; init; }
    public required string MyCollectionName { get; init; }

    MyDatabaseSettings IOptions<MyDatabaseSettings>.Value => this;

    public MigrationDescriptor GetMigratableDefinition()
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

Wait for migrations to complete using `IMongoMigrationCompletion` before accessing the database at any point.

-   `WaitAsync` may not ever be called in constructors.
-   `WaitAsync` is fast when the migration is completed, or the database is not configured.

```csharp
sealed class MyRepository(IOptions<MyDatabaseSettings> databaseSettings, IMongoMigrationCompletion migrationCompletion) {
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

Add `IMongoMigration`s between version 0 and 1 and so on.

-   The alias used for the `MongoMigrationAttribute` must match the name in `IMongoMigratable.GetMigrationSettings().Database.Alias`.

```csharp
[MongoMigration("MyDatabase", 0, 1, Description = "Add composite index to MyCollection")]
public sealed class PoeNinjaAddCompositeIndexMigration(IOptions<MyDatabaseSettings> optionsAccessor) : IMongoMigration
{
    public async Task DownAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
    }

    public async Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
    }
}
```

## Fixing database versions

A fixed database version ensures that the migration always produces a database of the specified version, even if the initial version was higher than the fixed version.

-   Fixing upgrades the database, if the initial version is lower than the fixed version.
-   Fixing downgrades the database, if the initial version is higher than the fixed version.
-   Fixing the database to a version is especially useful for a procedural feature rollout.

Fix a specific version by setting the `long? MigrateToFixedVersion` property in your `IMongoMigratable`; if the property is `null` - by default - a migration to the latest version will occur.

```csharp
public MigrationDescriptor GetMigrationSettings()
{
    return new()
    {
        ConnectionString = ConnectionString,
        Database = new("MyDatabase", DatabaseName),
        MirgrationStateCollectionName = "DATABASE_MIGRATIONS",
        MigrateToFixedVersion = 42069,
    };
}
```

## Migrations without ASP.NET DI

MongoDB Migration extensively uses ASP.NET to prepare the environment required for processing of migrations. Without ASP.NET most features are unavailable, but the core - `DatabaseMirationProcessor` - public API; it is accessible to the user.

1.  Describe your database in the `MigrationDescriptor` record.
2.  Pack all available `IMongoMigration`s into the `DatabaseMigrationProcessor` record.
3.  Instantiate `DatabaseMirationProcessor` for your database.
4.  Call `DatabaseMirationProcessor.MigrateToVersionAsync` with the available `DatabaseMigrationProcessor`s.

## ToDO

-   [x] Allow upgrading the database to the latest version.
-   [x] Allow upgrading the database to a specific version.
-   [x] Allow downgrading the database to a specific version.
-   [ ] Add test cases.

## Disclaimer

`csharp-mongo-migration` is not affiliated with or endorsed by [MongoDB](https://www.mongodb.com). MONGODB & MONGO are registered trademarks of MongoDB, Inc.
