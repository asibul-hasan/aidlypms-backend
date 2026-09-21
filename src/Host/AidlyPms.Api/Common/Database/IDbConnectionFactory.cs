using System.Data;
using Npgsql;

namespace AidlyPms.Api.Common.Database;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
    Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}

public class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public NpgsqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("DefaultConnection connection string not found");
    }

    public NpgsqlConnection CreateConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }

    public async Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        return conn;
    }
}
