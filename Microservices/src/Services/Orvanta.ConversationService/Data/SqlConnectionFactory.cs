
using System.Data;
using Microsoft.Data.SqlClient;

namespace Orvanta.ConversationService.Data;

public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("Orvanta")
            ?? throw new InvalidOperationException(
                "Connection string 'Orvanta' is missing.");
    }

    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }
}
