
using Dapper;
using Orvanta.ConversationService.Data;
using Orvanta.ConversationService.IInterfaces;
using Orvanta.ConversationService.Models;

namespace Orvanta.ConversationService.Repositories;

public sealed class ConversationRepository : IConversationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ConversationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> CreateConversationAsync(
        string title,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.Conversations
                (ConversationId, Title, CreatedAt, UpdatedAt)
            OUTPUT INSERTED.ConversationId
            VALUES
                (@ConversationId, @Title, SYSUTCDATETIME(), SYSUTCDATETIME());
            """;

        var conversationId = Guid.NewGuid();

        using var connection = _connectionFactory.CreateConnection();

        var command = new CommandDefinition(
            sql,
            new { ConversationId = conversationId, Title = title },
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(command);
    }

    public async Task<bool> ConversationExistsAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CAST(
                CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.Conversations
                    WHERE ConversationId = @ConversationId
                ) THEN 1 ELSE 0 END
            AS BIT);
            """;

        using var connection = _connectionFactory.CreateConnection();

        var command = new CommandDefinition(
            sql,
            new { ConversationId = conversationId },
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<bool>(command);
    }

    public async Task SaveMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.Messages
                (ConversationId, Role, Content, CreatedAt)
            VALUES
                (@ConversationId, @Role, @Content, SYSUTCDATETIME());

            UPDATE dbo.Conversations
            SET UpdatedAt = SYSUTCDATETIME()
            WHERE ConversationId = @ConversationId;
            """;

        using var connection = _connectionFactory.CreateConnection();

        var command = new CommandDefinition(
            sql,
            message,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }

    public async Task<IEnumerable<Conversation>> GetConversationsAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ConversationId, Title, CreatedAt, UpdatedAt
            FROM dbo.Conversations
            ORDER BY UpdatedAt DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();

        var command = new CommandDefinition(
            sql,
            cancellationToken: cancellationToken);

        return await connection.QueryAsync<Conversation>(command);
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT MessageId, ConversationId, Role, Content, CreatedAt
            FROM dbo.Messages
            WHERE ConversationId = @ConversationId
            ORDER BY MessageId ASC;
            """;

        using var connection = _connectionFactory.CreateConnection();

        var command = new CommandDefinition(
            sql,
            new { ConversationId = conversationId },
            cancellationToken: cancellationToken);

        return await connection.QueryAsync<ChatMessage>(command);
    }
}
