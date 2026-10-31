using Npgsql;
using System.Web;

namespace MyFinances.Services
{
    /// <summary>
    /// Turns the postgres:// address a host such as Render hands out
    /// into the Host=...;Database=... form Npgsql reads. Npgsql does
    /// not accept the address form itself, and a hosting dashboard
    /// cannot assemble the other form out of the parts it holds.
    /// </summary>
    public static class PostgresUrl
    {
        public static string? ToConnectionString(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            var isAddress =
                url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

            if (!isAddress)
                return url;

            var uri = new Uri(url);

            var credentials = uri.UserInfo.Split(':', 2);

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = Uri.UnescapeDataString(
                    uri.AbsolutePath.TrimStart('/')),
                Username = Uri.UnescapeDataString(credentials[0]),
                Password = credentials.Length > 1
                    ? Uri.UnescapeDataString(credentials[1])
                    : null
            };

            var sslMode = HttpUtility.ParseQueryString(uri.Query)["sslmode"];

            if (sslMode != null &&
                Enum.TryParse<SslMode>(sslMode, ignoreCase: true, out var mode))
            {
                builder.SslMode = mode;
            }

            return builder.ConnectionString;
        }
    }
}
