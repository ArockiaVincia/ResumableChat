using Microsoft.AspNetCore.Mvc;
using ResumableChat.Api.Models;
using ResumableChat.Api.Services;

namespace ResumableChat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConversationsController : ControllerBase
{
    private readonly IChatService _chatService;

    public ConversationsController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpPost]
    public async Task<ActionResult<CreateConversationResponse>> CreateConversation(CancellationToken ct)
    {
        var conversation = await _chatService.CreateConversationAsync(null, ct);
        return CreatedAtAction(nameof(CreateConversation), new { id = conversation.Id }, 
            new CreateConversationResponse(conversation.Id, conversation.CreatedAt));
    }

    [HttpPost("{conversationId}/messages")]
    public async Task<ActionResult<RunResponse>> PostMessage(
        string conversationId,
        [FromBody] PostMessageRequest request,
        CancellationToken ct)
    {
        try
        {
            var run = await _chatService.StartRunAsync(conversationId, request, ct);
            return Accepted($"/api/runs/{run.Id}", new RunResponse(
                run.Id,
                run.ConversationId,
                run.UserMessageId,
                run.Status,
                run.CreatedAt,
                run.CompletedAt
            ));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
