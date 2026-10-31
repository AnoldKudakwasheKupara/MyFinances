using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MyFinances.Data;

namespace MyFinances.Tests
{
    /// <summary>
    /// A throwaway SQL Server database for the whole test run, made from
    /// the same migrations as production and dropped at the end. The
    /// connection comes from MYFINANCES_TEST_DB, or else the web
    /// project's connection string (user secrets, then appsettings.json)
    /// with the database name swapped, so the real development data is
    /// never touched.
    /// </summary>
    public sealed class TestDatabase : IAsyncLifetime
    {
        public string ConnectionString { get; }

        public TestDatabase()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddUserSecrets<TestDatabase>(optional: true)
                .AddEnvironmentVariables()
                .Build();

            var source = configuration["MYFINANCES_TEST_DB"]
                ?? configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "The tests need SQL Server. Set MYFINANCES_TEST_DB to a connection string whose login " +
                    "can create databases, or set up the web project as described in README.md.");

            var builder = new SqlConnectionStringBuilder(source)
            {
                InitialCatalog = $"myfinances_test_{Guid.NewGuid():N}"[..40]
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
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options, user);
    }

    [CollectionDefinition(Name)]
    public class DatabaseCollection : ICollectionFixture<TestDatabase>
    {
        public const string Name = "Database";
    }
}
