using ResumableChat.Api.Data.Entities;
using ResumableChat.Api.Models;

namespace ResumableChat.Api.Services;

public interface IChatService
{
    Task<Conversation> CreateConversationAsync(string? id = null, CancellationToken ct = default);
    Task<Conversation?> GetConversationAsync(string id, CancellationToken ct = default);
    Task<Run> StartRunAsync(string conversationId, PostMessageRequest request, CancellationToken ct = default);
    Task<Run?> GetRunAsync(string runId, CancellationToken ct = default);
    Task<IReadOnlyList<ChatEvent>> GetEventsAsync(string runId, long afterCursor = 0, CancellationToken ct = default);
    IAsyncEnumerable<ChatEvent> StreamRunEventsAsync(string runId, long afterCursor = 0, CancellationToken ct = default);
}
