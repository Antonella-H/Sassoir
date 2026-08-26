using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

namespace Sassoir.Api.Infrastructure;

public static class ApiConfiguration
{
    public static string? NormalizePostgresConnectionString(string? connectionString, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;
        var maxPoolSize = Math.Max(5, configuration.GetValue("Database:MaxPoolSize", 20));
        var commandTimeout = Math.Max(5, configuration.GetValue("Database:CommandTimeoutSeconds", 30));

        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var existingBuilder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Pooling = true,
                MaxPoolSize = maxPoolSize,
                CommandTimeout = commandTimeout
            };
            return existingBuilder.ConnectionString;
        }

        var uri = new Uri(connectionString);
        var credentials = uri.UserInfo.Split(':', 2);
        var database = uri.AbsolutePath.TrimStart('/');

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(database),
            Username = credentials.Length > 0 ? Uri.UnescapeDataString(credentials[0]) : string.Empty,
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : string.Empty,
            SslMode = SslMode.Require,
            Pooling = true,
            MaxPoolSize = maxPoolSize,
            CommandTimeout = commandTimeout
        };

        return builder.ConnectionString;
    }

    public static string[] GetAllowedOrigins(IConfiguration configuration)
    {
        string[] defaultOrigins =
        [
            "https://sassoir.com",
            "https://www.sassoir.com",
            "http://127.0.0.1:5173",
            "http://localhost:5173"
        ];

        var configuredOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .GetChildren()
            .Select(origin => origin.Value)
            .Concat(ParseOrigins(configuration["Cors:AllowedOrigins"]))
            .Concat(ParseOrigins(configuration["CORS_ALLOWED_ORIGINS"]))
            .Concat(ParseOrigins(configuration["ALLOWED_ORIGINS"]));

        return defaultOrigins
            .Concat(configuredOrigins)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin!.Trim().Trim('"', '\'').TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static RateLimitPartition<string> FixedWindowPolicy(HttpContext context, string configKey, int fallbackPermitLimit)
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var permitLimit = Math.Max(1, configuration.GetValue(configKey, fallbackPermitLimit));
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    private static string[] ParseOrigins(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
