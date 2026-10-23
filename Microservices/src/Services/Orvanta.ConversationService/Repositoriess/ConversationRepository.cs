using System.Data;
using Dapper;
using Orvanta.ConversationService.Data;
using Orvanta.ConversationService.IInterfaces;
using Orvanta.ConversationService.Models;

namespace Orvanta.ConversationService.Repositories;

/// <summary>
/// Data access through the stored procedures in Microservices/Database/Orvanta/StoredProcedures.
/// </summary>
public sealed class ConversationRepository : IConversationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ConversationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> CreateConversationAsync(
        Guid userId,
        string title,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<Guid>(Procedure(
            "dbo.usp_Conversations_Create",
            new { ConversationId = Guid.NewGuid(), UserId = userId, Title = title },
            cancellationToken));
    }

    public async Task<bool> ConversationExistsAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        return await connection.ExecuteScalarAsync<bool>(Procedure(
            "dbo.usp_Conversations_Exists",
            new { ConversationId = conversationId, UserId = userId },
            cancellationToken));
    }

    public async Task SaveMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(Procedure(
            "dbo.usp_Messages_Save",
            new { message.ConversationId, message.Role, message.Content },
            cancellationToken));
    }

    public async Task<IEnumerable<Conversation>> GetConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        return await connection.QueryAsync<Conversation>(Procedure(
            "dbo.usp_Conversations_List",
            new { UserId = userId },
            cancellationToken));
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();

        return await connection.QueryAsync<ChatMessage>(Procedure(
            "dbo.usp_Messages_ListByConversation",
            new { ConversationId = conversationId },
            cancellationToken));
    }

    private static CommandDefinition Procedure(string name, object? parameters, CancellationToken cancellationToken) =>
        new(name, parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
}
