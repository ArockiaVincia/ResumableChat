using ResumableChat.Api.Data.Entities;

namespace ResumableChat.Api.Repositories;

public interface IChatRepository
{
    Task<Conversation> CreateConversationAsync(Conversation conversation, CancellationToken ct = default);
    Task<Conversation?> GetConversationAsync(string id, CancellationToken ct = default);
    
    Task<Run> CreateRunAsync(Run run, CancellationToken ct = default);
    Task<Run?> GetRunAsync(string id, CancellationToken ct = default);
    Task UpdateRunStatusAsync(string runId, string status, DateTimeOffset? completedAt = null, CancellationToken ct = default);
    
    Task<ChatEvent> AddEventAsync(ChatEvent chatEvent, CancellationToken ct = default);
    Task<IReadOnlyList<ChatEvent>> GetEventsAsync(string runId, long afterSequence = 0, CancellationToken ct = default);
    Task<long> GetMaxSequenceAsync(string runId, CancellationToken ct = default);
    Task<int> HandleInterruptedRunsOnStartupAsync(CancellationToken ct = default);
}
