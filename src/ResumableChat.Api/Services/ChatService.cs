using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResumableChat.Api.Data.Entities;
using ResumableChat.Api.Models;
using ResumableChat.Api.Repositories;

namespace ResumableChat.Api.Services;

public class ChatService : IChatService
{
    private readonly IChatRepository _repository;
    private readonly IFakeResponseGenerator _generator;
    private readonly IRunBroadcaster _broadcaster;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IChatRepository repository,
        IFakeResponseGenerator generator,
        IRunBroadcaster broadcaster,
        IServiceScopeFactory scopeFactory,
        ILogger<ChatService> logger)
    {
        _repository = repository;
        _generator = generator;
        _broadcaster = broadcaster;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<Conversation> CreateConversationAsync(string? id = null, CancellationToken ct = default)
    {
        var conversation = new Conversation
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return await _repository.CreateConversationAsync(conversation, ct);
    }

    public async Task<Conversation?> GetConversationAsync(string id, CancellationToken ct = default)
    {
        return await _repository.GetConversationAsync(id, ct);
    }

    public async Task<Run> StartRunAsync(string conversationId, PostMessageRequest request, CancellationToken ct = default)
    {
        var conversation = await _repository.GetConversationAsync(conversationId, ct);
        if (conversation == null)
        {
            throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");
        }

        var run = new Run
        {
            Id = Guid.NewGuid().ToString("N"),
            ConversationId = conversationId,
            UserMessageId = string.IsNullOrWhiteSpace(request.UserMessageId) ? Guid.NewGuid().ToString("N") : request.UserMessageId,
            Status = RunStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateRunAsync(run, ct);

        // Run the generation in background so it starts generating and persisting events
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessRunGenerationAsync(run.Id, request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in background generation for run {RunId}", run.Id);
            }
        });

        return run;
    }

    public async Task<Run?> GetRunAsync(string runId, CancellationToken ct = default)
    {
        return await _repository.GetRunAsync(runId, ct);
    }

    public async Task<IReadOnlyList<ChatEvent>> GetEventsAsync(string runId, long afterCursor = 0, CancellationToken ct = default)
    {
        return await _repository.GetEventsAsync(runId, afterCursor, ct);
    }

    public async IAsyncEnumerable<ChatEvent> StreamRunEventsAsync(
        string runId,
        long afterCursor = 0,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var run = await _repository.GetRunAsync(runId, ct);
        if (run == null)
        {
            throw new KeyNotFoundException($"Run '{runId}' not found.");
        }

        // 1. Subscribe to live broadcaster SYNCHRONOUSLY FIRST
        // This guarantees that any event published from this exact instant onward is buffered in Reader
        await using var subscription = _broadcaster.Subscribe(runId);

        long lastEmittedSequence = afterCursor;

        // 2. Fetch and yield existing persisted events from SQLite
        var persistedEvents = await _repository.GetEventsAsync(runId, lastEmittedSequence, ct);
        foreach (var evt in persistedEvents)
        {
            if (evt.Sequence > lastEmittedSequence)
            {
                lastEmittedSequence = evt.Sequence;
                yield return evt;

                if (evt.Type == ChatEventType.Done || evt.Type == ChatEventType.Error)
                {
                    yield break;
                }
            }
        }

        // 3. Yield live events from channel reader
        while (await subscription.Reader.WaitToReadAsync(ct))
        {
            while (subscription.Reader.TryRead(out var liveEvt))
            {
                if (liveEvt.Sequence > lastEmittedSequence)
                {
                    lastEmittedSequence = liveEvt.Sequence;
                    yield return liveEvt;

                    if (liveEvt.Type == ChatEventType.Done || liveEvt.Type == ChatEventType.Error)
                    {
                        yield break;
                    }
                }
            }
        }

        // 4. Fallback check: If the channel closed, fetch any final terminal events
        // (Done/Error) that were persisted to SQLite concurrently with channel closure.
        var remaining = await _repository.GetEventsAsync(runId, lastEmittedSequence, ct);
        foreach (var evt in remaining)
        {
            if (evt.Sequence > lastEmittedSequence)
            {
                lastEmittedSequence = evt.Sequence;
                yield return evt;

                if (evt.Type == ChatEventType.Done || evt.Type == ChatEventType.Error)
                {
                    yield break;
                }
            }
        }
    }

    private async Task ProcessRunGenerationAsync(string runId, PostMessageRequest request)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatRepository>();
        var generator = scope.ServiceProvider.GetRequiredService<IFakeResponseGenerator>();

        long sequence = 0;

        try
        {
            await foreach (var chunk in generator.GenerateResponseAsync(
                prompt: request.Content,
                failAfterCount: request.FailAfterCount,
                delayMs: request.DelayMs,
                targetChunkCount: request.TargetChunkCount,
                cancellationToken: CancellationToken.None))
            {
                sequence++;
                var chatEvent = new ChatEvent
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RunId = runId,
                    Sequence = sequence,
                    Type = ChatEventType.Text,
                    Text = chunk.Text,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                await repo.AddEventAsync(chatEvent);
                await _broadcaster.PublishEventAsync(runId, chatEvent);
            }

            // Successfully finished generation
            sequence++;
            var doneEvent = new ChatEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                RunId = runId,
                Sequence = sequence,
                Type = ChatEventType.Done,
                Text = string.Empty,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await repo.AddEventAsync(doneEvent);
            await _broadcaster.PublishEventAsync(runId, doneEvent);

            await repo.UpdateRunStatusAsync(runId, RunStatus.Completed, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Run {RunId} failed during generation at sequence {Sequence}", runId, sequence);
            
            sequence++;
            var errorEvent = new ChatEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                RunId = runId,
                Sequence = sequence,
                Type = ChatEventType.Error,
                Text = ex.Message,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await repo.AddEventAsync(errorEvent);
            await _broadcaster.PublishEventAsync(runId, errorEvent);

            await repo.UpdateRunStatusAsync(runId, RunStatus.Failed, DateTimeOffset.UtcNow);
        }
    }
}
