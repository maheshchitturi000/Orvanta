
using Orvanta.ConversationService.IInterfaces;
using Orvanta.ConversationService.Interfaces;
using Orvanta.ConversationService.Models;

namespace Orvanta.ConversationService.Services;

public sealed class ConversationService : IConversationService
{
    private readonly IConversationRepository _repository;

    public ConversationService(IConversationRepository repository)
    {
        _repository = repository;
    }

    public Task<Guid> CreateConversationAsync(
        Guid userId,
        string title,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));

        return _repository.CreateConversationAsync(
            userId, title.Trim(), cancellationToken);
    }

    public async Task<bool> SaveMessageAsync(
        Guid userId,
        Guid conversationId,
        string role,
        string content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Message content is required.");

        if (role is not ("user" or "assistant" or "system"))
            throw new ArgumentException("Invalid message role.");

        if (!await _repository.ConversationExistsAsync(
                userId, conversationId, cancellationToken))
            return false;

        var message = new ChatMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content.Trim()
        };

        await _repository.SaveMessageAsync(message, cancellationToken);
        return true;
    }

    public Task<IEnumerable<Conversation>> GetConversationsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return _repository.GetConversationsAsync(userId, cancellationToken);
    }

    public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (!await _repository.ConversationExistsAsync(
                userId, conversationId, cancellationToken))
            throw new KeyNotFoundException("Conversation not found.");

        return await _repository.GetMessagesAsync(
            conversationId, cancellationToken);
    }
}
