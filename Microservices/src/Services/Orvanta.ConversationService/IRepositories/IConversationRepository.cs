
using Orvanta.ConversationService.Models;

namespace Orvanta.ConversationService.IInterfaces;

public interface IConversationRepository
{
    Task<Guid> CreateConversationAsync(
        string title,
        CancellationToken cancellationToken);

    Task<bool> ConversationExistsAsync(
        Guid conversationId,
        CancellationToken cancellationToken);

    Task SaveMessageAsync(
        ChatMessage message,
        CancellationToken cancellationToken);

    Task<IEnumerable<Conversation>> GetConversationsAsync(
        CancellationToken cancellationToken);

    Task<IEnumerable<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken);
}
