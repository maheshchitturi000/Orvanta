
namespace Orvanta.ConversationService.Models;

public sealed class Conversation
{
    public Guid ConversationId { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
