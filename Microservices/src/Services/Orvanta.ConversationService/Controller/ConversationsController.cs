
using Microsoft.AspNetCore.Mvc;
using Orvanta.ConversationService.DTOs;
using Orvanta.ConversationService.Interfaces;

namespace Orvanta.ConversationService.Controllers;

[ApiController]
[Route("api/conversations")]
public sealed class ConversationsController : ControllerBase
{
    private readonly IConversationService _service;

    public ConversationsController(IConversationService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateConversationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Title is required." });

        var id = await _service.CreateConversationAsync(
            request.Title, cancellationToken);

        return Created($"/api/conversations/{id}", new
        {
            conversationId = id,
            title = request.Title.Trim()
        });
    }

    [HttpPost("{conversationId:guid}/messages")]
    public async Task<IActionResult> SaveMessage(
        Guid conversationId,
        [FromBody] SaveMessageRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await _service.SaveMessageAsync(
            conversationId, request.Role, request.Content,
            cancellationToken);

        return saved
            ? Ok(new { message = "Message saved." })
            : NotFound(new { error = "Conversation not found." });
    }

    [HttpGet]
    public async Task<IActionResult> GetConversations(
        CancellationToken cancellationToken)
    {
        var conversations =
            await _service.GetConversationsAsync(cancellationToken);

        return Ok(conversations);
    }

    [HttpGet("{conversationId:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var messages = await _service.GetMessagesAsync(
                conversationId, cancellationToken);

            return Ok(messages);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Conversation not found." });
        }
    }
}
