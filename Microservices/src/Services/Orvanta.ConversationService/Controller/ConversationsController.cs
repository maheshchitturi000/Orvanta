
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orvanta.ConversationService.DTOs;
using Orvanta.ConversationService.Interfaces;
using Orvanta.Shared.Security;

namespace Orvanta.ConversationService.Controllers;

[ApiController]
[Authorize(Policy = "UserOnly")]
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
            User.GetUserId(), request.Title, cancellationToken);

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
            User.GetUserId(), conversationId, request.Role, request.Content,
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
            await _service.GetConversationsAsync(User.GetUserId(), cancellationToken);

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
                User.GetUserId(), conversationId, cancellationToken);

            return Ok(messages);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Conversation not found." });
        }
    }
}
