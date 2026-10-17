
using Orvanta.ConversationService.Models;

namespace Orvanta.ConversationService.Interfaces;

public interface IConversationService
{
    Task<Guid> CreateConversationAsync(
        string title,
        CancellationToken cancellationToken);

    Task<bool> SaveMessageAsync(
        Guid conversationId,
        string role,
        string content,
        CancellationToken cancellationToken);

    Task<IEnumerable<Conversation>> GetConversationsAsync(
        CancellationToken cancellationToken);

    Task<IEnumerable<ChatMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken);
}
