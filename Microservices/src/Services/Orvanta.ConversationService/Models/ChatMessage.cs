
namespace Orvanta.ConversationService.Models;

public sealed class ChatMessage
{
    public long MessageId { get; set; }

    public Guid ConversationId { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
