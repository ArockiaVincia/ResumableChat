using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ResumableChat.Api.Models;
using ResumableChat.Api.Services;

namespace ResumableChat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RunsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IChatService _chatService;

    public RunsController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpGet("{runId}")]
    public async Task<ActionResult<RunDetailResponse>> GetRun(string runId, CancellationToken ct)
    {
        var run = await _chatService.GetRunAsync(runId, ct);
        if (run == null)
        {
            return NotFound(new { message = $"Run '{runId}' not found." });
        }

        var eventResponses = run.Events.Select(e => new ChatEventResponse(
            e.Id,
            e.RunId,
            e.Sequence,
            e.Type,
            e.Text,
            e.CreatedAt
        )).ToList();

        return Ok(new RunDetailResponse(
            run.Id,
            run.ConversationId,
            run.UserMessageId,
            run.Status,
            run.CreatedAt,
            run.CompletedAt,
            eventResponses.Count,
            eventResponses
        ));
    }

    [HttpGet("{runId}/events")]
    public async Task<ActionResult<List<ChatEventResponse>>> GetEvents(
        string runId,
        [FromQuery] long after = 0,
        CancellationToken ct = default)
    {
        var run = await _chatService.GetRunAsync(runId, ct);
        if (run == null)
        {
            return NotFound(new { message = $"Run '{runId}' not found." });
        }

        if (after < 0)
        {
            return Conflict(new { error = "InvalidCursor", message = "Cursor cannot be negative." });
        }

        var maxSequence = run.Events.Count > 0 ? run.Events.Max(e => e.Sequence) : 0;
        if (after > maxSequence)
        {
            return Conflict(new
            {
                error = "StaleCursor",
                message = $"Requested cursor {after} exceeds available sequence {maxSequence}.",
                maxSequence
            });
        }

        var events = await _chatService.GetEventsAsync(runId, after, ct);

        var eventResponses = events.Select(e => new ChatEventResponse(
            e.Id,
            e.RunId,
            e.Sequence,
            e.Type,
            e.Text,
            e.CreatedAt
        )).ToList();

        return Ok(eventResponses);
    }

    [HttpGet("{runId}/stream")]
    public async Task Stream(
        string runId,
        [FromQuery] long after = 0)
    {
        var run = await _chatService.GetRunAsync(runId, HttpContext.RequestAborted);
        if (run == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await Response.WriteAsJsonAsync(new { message = $"Run '{runId}' not found." }, HttpContext.RequestAborted);
            return;
        }

        // Validate cursor
        if (after < 0)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            await Response.WriteAsJsonAsync(new
            {
                error = "InvalidCursor",
                message = "Cursor cannot be negative."
            }, HttpContext.RequestAborted);
            return;
        }

        var maxSequence = run.Events.Count > 0 ? run.Events.Max(e => e.Sequence) : 0;
        if (after > maxSequence)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            await Response.WriteAsJsonAsync(new
            {
                error = "StaleCursor",
                message = $"Requested cursor {after} exceeds available sequence {maxSequence} for run '{runId}'.",
                maxSequence
            }, HttpContext.RequestAborted);
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream; charset=utf-8");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            await foreach (var chatEvent in _chatService.StreamRunEventsAsync(runId, after, HttpContext.RequestAborted))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    eventId = chatEvent.Id,
                    runId = chatEvent.RunId,
                    sequence = chatEvent.Sequence,
                    type = chatEvent.Type,
                    text = chatEvent.Text,
                    createdAt = chatEvent.CreatedAt
                }, JsonOptions);

                var sseMessage = $"id: {chatEvent.Sequence}\nevent: {chatEvent.Type}\ndata: {payload}\n\n";
                await Response.WriteAsync(sseMessage, HttpContext.RequestAborted);
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client closed the connection / disconnected
        }
    }
}
