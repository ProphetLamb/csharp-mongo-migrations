using MongoDB.Migration;
using MongoDB.Migration.Core;
using MongoDB.Migration.Text.Integration;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .Configure<DatabaseSettings>(o => { })
    .AddMigrations()
    .AddHostedService<TestService>();

var app = builder.Build();
app.Run();

namespace MongoDB.Migration.Text.Integration
{
    public sealed class DatabaseSettings : IMongoMigratable
    {
        public const string Alias = "TestDatabase";

        public string DatabaseName { get; init; } = $"TestDatabase{Environment.CurrentManagedThreadId}";

        public MongoMigrableDefinition GetMigratableDefinition()
        {
            return new()
            {
                ConnectionString = "mongodb://localhost:27017",
                Database = new(Alias, DatabaseName),
                MirgrationStateCollectionName = "MIGRATION_COLLECTION"
            };
        }
    }

    public sealed class TestService(IMongoMigrationCompletion completion) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
                var result = await completion.WaitAsync(DatabaseSettings.Alias, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Environment.Exit(1);
            }
            Environment.Exit(0);
        }
    }
}
