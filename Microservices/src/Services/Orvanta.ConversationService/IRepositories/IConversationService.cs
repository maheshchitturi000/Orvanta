
using Orvanta.ConversationService.Models;

namespace Orvanta.ConversationService.Interfaces;

public interface IConversationService
{
    Task<Guid> CreateConversationAsync(
        Guid userId,
        string title,
        CancellationToken cancellationToken);

    Task<bool> SaveMessageAsync(
        Guid userId,
        Guid conversationId,
        string role,
        string content,
        CancellationToken cancellationToken);

    Task<IEnumerable<Conversation>> GetConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<IEnumerable<ChatMessage>> GetMessagesAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken);
}
