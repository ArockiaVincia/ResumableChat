using Microsoft.EntityFrameworkCore;
using ResumableChat.Api.Data;
using ResumableChat.Api.Data.Entities;

namespace ResumableChat.Api.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly ChatDbContext _context;

    public ChatRepository(ChatDbContext context)
    {
        _context = context;
    }

    public async Task<Conversation> CreateConversationAsync(Conversation conversation, CancellationToken ct = default)
    {
        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task<Conversation?> GetConversationAsync(string id, CancellationToken ct = default)
    {
        return await _context.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<Run> CreateRunAsync(Run run, CancellationToken ct = default)
    {
        _context.Runs.Add(run);
        await _context.SaveChangesAsync(ct);
        return run;
    }

    public async Task<Run?> GetRunAsync(string id, CancellationToken ct = default)
    {
        return await _context.Runs
            .AsNoTracking()
            .Include(r => r.Events.OrderBy(e => e.Sequence))
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task UpdateRunStatusAsync(string runId, string status, DateTimeOffset? completedAt = null, CancellationToken ct = default)
    {
        var run = await _context.Runs.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run != null)
        {
            run.Status = status;
            if (completedAt.HasValue)
            {
                run.CompletedAt = completedAt.Value;
            }
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<ChatEvent> AddEventAsync(ChatEvent chatEvent, CancellationToken ct = default)
    {
        _context.Events.Add(chatEvent);
        await _context.SaveChangesAsync(ct);
        return chatEvent;
    }

    public async Task<IReadOnlyList<ChatEvent>> GetEventsAsync(string runId, long afterSequence = 0, CancellationToken ct = default)
    {
        return await _context.Events
            .AsNoTracking()
            .Where(e => e.RunId == runId && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);
    }

    public async Task<long> GetMaxSequenceAsync(string runId, CancellationToken ct = default)
    {
        var max = await _context.Events
            .Where(e => e.RunId == runId)
            .Select(e => (long?)e.Sequence)
            .MaxAsync(ct);

        return max ?? 0;
    }

    public async Task<int> HandleInterruptedRunsOnStartupAsync(CancellationToken ct = default)
    {
        var inProgressRuns = await _context.Runs
            .Where(r => r.Status == RunStatus.Running)
            .ToListAsync(ct);

        foreach (var run in inProgressRuns)
        {
            run.Status = RunStatus.Interrupted;
            run.CompletedAt = DateTimeOffset.UtcNow;

            var maxSeq = await _context.Events
                .Where(e => e.RunId == run.Id)
                .Select(e => (long?)e.Sequence)
                .MaxAsync(ct) ?? 0;

            _context.Events.Add(new ChatEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                RunId = run.Id,
                Sequence = maxSeq + 1,
                Type = ChatEventType.Error,
                Text = "Run was interrupted due to service restart.",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        if (inProgressRuns.Count > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return inProgressRuns.Count;
    }
}
