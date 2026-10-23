
using Orvanta.ConversationService.Models;

namespace Orvanta.ConversationService.IInterfaces;

public interface IConversationRepository
{
    Task<Guid> CreateConversationAsync(
        Guid userId,
        string title,
        CancellationToken cancellationToken);

    Task<bool> ConversationExistsAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken);

    Task SaveMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken);

    Task<IEnumerable<Conversation>> GetConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<IEnumerable<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken);
}
