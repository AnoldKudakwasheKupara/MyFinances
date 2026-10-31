using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MyFinances.Data;
using MyFinances.Services;
using Npgsql;

namespace MyFinances.Tests
{
    /// <summary>
    /// A throwaway PostgreSQL database for the whole test run, made from
    /// the same migrations as production and dropped at the end. The
    /// connection comes from MYFINANCES_TEST_DB, or else the web
    /// project's user secrets with the database name swapped, so the
    /// real development data is never touched.
    /// </summary>
    public sealed class TestDatabase : IAsyncLifetime
    {
        public string ConnectionString { get; }

        public TestDatabase()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<TestDatabase>(optional: true)
                .AddEnvironmentVariables()
                .Build();

            var source = configuration["MYFINANCES_TEST_DB"]
                ?? PostgresUrl.ToConnectionString(configuration.GetConnectionString("DefaultConnection"))
                ?? throw new InvalidOperationException(
                    "The tests need PostgreSQL. Set MYFINANCES_TEST_DB to a connection string whose login " +
                    "can create databases, or set the web project's user secret as described in README.md.");

            var builder = new NpgsqlConnectionStringBuilder(source)
            {
                Database = $"myfinances_test_{Guid.NewGuid():N}"[..40]
            };

            ConnectionString = builder.ConnectionString;
        }

        public async Task InitializeAsync()
        {
            await using var context = NewContext(new CurrentUser(null));
            await context.Database.MigrateAsync();
        }

        public async Task DisposeAsync()
        {
            await using var context = NewContext(new CurrentUser(null));
            await context.Database.EnsureDeletedAsync();
        }

        public ApplicationDbContext NewContext(CurrentUser user) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options, user);
    }

    [CollectionDefinition(Name)]
    public class DatabaseCollection : ICollectionFixture<TestDatabase>
    {
        public const string Name = "Database";
    }
}
