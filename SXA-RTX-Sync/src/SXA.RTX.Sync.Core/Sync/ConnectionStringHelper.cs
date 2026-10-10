using Microsoft.Data.SqlClient;

namespace SXA.RTX.Sync.Core.Sync;

public static class ConnectionStringHelper
{
    public const int DefaultMaxPoolSize = 10;
    public const int DefaultConnectTimeoutSeconds = 8;

    public static string Harden(string connectionString,
        int maxPoolSize = DefaultMaxPoolSize,
        int connectTimeoutSeconds = DefaultConnectTimeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);

            if (builder.MaxPoolSize <= 0 || builder.MaxPoolSize > maxPoolSize)
            {
                builder.MaxPoolSize = maxPoolSize;
            }

            if (builder.ConnectTimeout <= 0 || builder.ConnectTimeout > connectTimeoutSeconds)
            {
                builder.ConnectTimeout = connectTimeoutSeconds;
            }

            return builder.ConnectionString;
        }
        catch
        {
            return connectionString;
        }
    }

    public static bool IsLoginFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number == 18456)
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class SyncAuthenticationException : Exception
{
    public SyncAuthenticationException(string message, Exception? inner = null) : base(message, inner) { }
}
