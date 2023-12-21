using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Migration;
using MongoDB.Migration.Core;
using MongoDB.Migration.Text.Integration;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .Configure<DatabaseSettings>(o => { })
    .AddHostedService<TestService>()
    .AddMigrations();

var app = builder.Build();
app.Run();

namespace MongoDB.Migration.Text.Integration
{
    public sealed class DatabaseSettings : IMongoMigratable
    {
        public const string Alias = "TestDatabase";

        public string DataModelCollectionName { get; init; } = "DataModel";
        public string ConnectionString { get; init; } = "mongodb://mongo:mongo@localhost:27017";
        public string DatabaseName { get; init; } = $"TestDatabase{Environment.CurrentManagedThreadId}";

        public MongoMigrableDefinition GetMigratableDefinition()
        {
            return new()
            {
                ConnectionString = ConnectionString,
                Database = new(Alias, DatabaseName),
                MirgrationStateCollectionName = "MIGRATION_COLLECTION"
            };
        }
    }

    public sealed record DataModel(ObjectId Id, string DistinctText);

    [MongoMigration(DatabaseSettings.Alias, 0, 1, Description = $"Add composite index")]
    public sealed class TestMigration(IOptions<DatabaseSettings> options) : IMongoMigration
    {
        public const string DistinctTextIdentifier = "DistinctTextIdentifier";

        private IMongoCollection<DataModel> GetGemPriceCollection(IMongoDatabase database)
        {
            return database.GetCollection<DataModel>(options.Value.DataModelCollectionName);
        }
        public async Task DownAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
        {
            var col = GetGemPriceCollection(database);
            await col.Indexes.DropOneAsync(DistinctTextIdentifier, cancellationToken).ConfigureAwait(false);
        }

        public async Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
        {
            var col = GetGemPriceCollection(database);
            IndexKeysDefinitionBuilder<DataModel> builder = new();
            var index = builder.Text(m => m.DistinctText);
            CreateIndexModel<DataModel> model = new(index, new()
            {
                Unique = true,
                Name = DistinctTextIdentifier
            });
            _ = await col.Indexes.CreateOneAsync(model, null, cancellationToken).ConfigureAwait(false);
        }
    }

    public sealed class TestService(IMongoMigrationCompletion completion, IOptions<DatabaseSettings> options) : BackgroundService
    {
        private readonly IMongoCollection<DataModel> _col = new MongoClient(options.Value.ConnectionString)
                .GetDatabase(options.Value.DatabaseName)
                .GetCollection<DataModel>(options.Value.DataModelCollectionName);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
                var result = await completion.WaitAsync(DatabaseSettings.Alias, cts.Token).ConfigureAwait(false);
                var cur = await _col.Indexes.ListAsync(stoppingToken).ConfigureAwait(false);
                var indices = await cur.ToListAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Environment.Exit(1);
            }
            Environment.Exit(0);
        }
    }
}
