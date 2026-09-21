using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumableChat.Api.Controllers;
using ResumableChat.Api.Data;
using ResumableChat.Api.Data.Entities;
using ResumableChat.Api.Models;
using ResumableChat.Api.Repositories;
using ResumableChat.Api.Services;

namespace ResumableChat.Tests;

public class ReconnectAndRecoveryTests : IDisposable
{
    private readonly string _dbName;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public ReconnectAndRecoveryTests()
    {
        _dbName = $"reconnect_test_{Guid.NewGuid():N}.db";

        var services = new ServiceCollection();
        services.AddDbContext<ChatDbContext>(opts => opts.UseSqlite($"Data Source={_dbName}"));
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddSingleton<IFakeResponseGenerator, FakeResponseGenerator>();
        services.AddSingleton<IRunBroadcaster, RunBroadcaster>();
        services.AddScoped<IChatService, ChatService>();
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        _scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Test1_ReplayAfterCursor_ReturnsOnlySubsequentEvents()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatRepository>();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        var conv = await repo.CreateConversationAsync(new Conversation { Id = "conv_t1", CreatedAt = DateTimeOffset.UtcNow });
        var run = await repo.CreateRunAsync(new Run
        {
            Id = "run_t1",
            ConversationId = conv.Id,
            UserMessageId = "msg_t1",
            Status = RunStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });

        for (int i = 1; i <= 6; i++)
        {
            await repo.AddEventAsync(new ChatEvent
            {
                Id = $"evt_t1_{i}",
                RunId = run.Id,
                Sequence = i,
                Type = (i == 6) ? ChatEventType.Done : ChatEventType.Text,
                Text = (i == 6) ? "" : $"word_{i} ",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        // Request events after cursor = 3
        var replayedEvents = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamRunEventsAsync(run.Id, afterCursor: 3))
        {
            replayedEvents.Add(evt);
        }

        Assert.Equal(3, replayedEvents.Count);
        Assert.Equal(4, replayedEvents[0].Sequence);
        Assert.Equal(5, replayedEvents[1].Sequence);
        Assert.Equal(6, replayedEvents[2].Sequence);
        Assert.Equal(ChatEventType.Done, replayedEvents[2].Type);
    }

    [Fact]
    public async Task Test2_ReplayLiveOverlap_DeliversStrictMonotonicOrderWithoutDuplicates()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatRepository>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<IRunBroadcaster>();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        var conv = await repo.CreateConversationAsync(new Conversation { Id = "conv_t2", CreatedAt = DateTimeOffset.UtcNow });
        var run = await repo.CreateRunAsync(new Run
        {
            Id = "run_t2",
            ConversationId = conv.Id,
            UserMessageId = "msg_t2",
            Status = RunStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Pre-persist events 1, 2, 3 in SQLite
        for (int i = 1; i <= 3; i++)
        {
            await repo.AddEventAsync(new ChatEvent
            {
                Id = $"evt_db_{i}",
                RunId = run.Id,
                Sequence = i,
                Type = ChatEventType.Text,
                Text = $"chunk_{i} ",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        // Start streaming concurrently
        var deliveredEvents = new List<ChatEvent>();
        var streamTask = Task.Run(async () =>
        {
            await foreach (var evt in chatService.StreamRunEventsAsync(run.Id, afterCursor: 0))
            {
                deliveredEvents.Add(evt);
            }
        });

        // Simulate overlapping live events (publish 3, 4, 5, 6 to broadcaster, where 3 overlaps with DB!)
        await Task.Delay(30);
        for (int i = 3; i <= 6; i++)
        {
            var isDone = (i == 6);
            var liveEvt = new ChatEvent
            {
                Id = $"evt_live_{i}",
                RunId = run.Id,
                Sequence = i,
                Type = isDone ? ChatEventType.Done : ChatEventType.Text,
                Text = isDone ? "" : $"chunk_{i} ",
                CreatedAt = DateTimeOffset.UtcNow
            };
            if (i > 3)
            {
                await repo.AddEventAsync(liveEvt);
            }
            await broadcaster.PublishEventAsync(run.Id, liveEvt);
        }

        await streamTask;

        // Verify: delivered events are 1, 2, 3, 4, 5, 6 with NO duplicate for event 3
        Assert.Equal(6, deliveredEvents.Count);
        for (int i = 0; i < deliveredEvents.Count; i++)
        {
            Assert.Equal(i + 1, deliveredEvents[i].Sequence);
        }
    }

    [Fact]
    public void Test3_ClientDeduplication_DiscardsEventsWithSequenceLessThanOrEqualToLastProcessed()
    {
        // Model the client-side deduplication logic
        long lastProcessedSequence = 0;
        var displayedChunks = new List<string>();

        void ClientOnEvent(long sequence, string text)
        {
            if (sequence <= lastProcessedSequence)
            {
                // Discard duplicate!
                return;
            }
            lastProcessedSequence = sequence;
            displayedChunks.Add(text);
        }

        // Simulate incoming stream containing duplicate replay events
        ClientOnEvent(1, "The ");
        ClientOnEvent(2, "quick ");
        ClientOnEvent(3, "brown ");
        // Duplicate events received due to connection overlap:
        ClientOnEvent(2, "quick ");
        ClientOnEvent(3, "brown ");
        // New events:
        ClientOnEvent(4, "fox ");
        ClientOnEvent(4, "fox ");
        ClientOnEvent(5, "jumps.");

        Assert.Equal(5, displayedChunks.Count);
        Assert.Equal(5, lastProcessedSequence);
        var reconstructed = string.Join("", displayedChunks);
        Assert.Equal("The quick brown fox jumps.", reconstructed);
    }

    [Fact]
    public async Task Test4_ReconnectUsesExistingRun_PreservesRunAndConversationIdentity()
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChatRepository>();

        var conversation = await chatService.CreateConversationAsync();
        var request = new PostMessageRequest(
            UserMessageId: "msg_reconnect_test",
            Content: "Preserve Run Test",
            DelayMs: 0,
            TargetChunkCount: 10
        );

        var run = await chatService.StartRunAsync(conversation.Id, request);
        var initialRunId = run.Id;
        var initialConvId = run.ConversationId;

        // Simulate client receiving first 3 events then disconnecting
        var firstStreamEvents = new List<ChatEvent>();
        using var cts = new CancellationTokenSource();
        await foreach (var evt in chatService.StreamRunEventsAsync(run.Id, afterCursor: 0, cts.Token))
        {
            firstStreamEvents.Add(evt);
            if (firstStreamEvents.Count == 3)
            {
                cts.Cancel(); // Simulate network drop
                break;
            }
        }
        Assert.Equal(3, firstStreamEvents.Count);

        // Reconnect to the SAME runId at cursor = 3
        var secondStreamEvents = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamRunEventsAsync(initialRunId, afterCursor: 3))
        {
            secondStreamEvents.Add(evt);
        }

        // Verify all subsequent events recovered
        Assert.NotEmpty(secondStreamEvents);
        Assert.Equal(4, secondStreamEvents[0].Sequence);

        // Verify NO new run was created: conversation still has exactly 1 run with initialRunId
        using var verifyScope = _scopeFactory.CreateScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<IChatRepository>();
        var dbRun = await verifyRepo.GetRunAsync(initialRunId);

        Assert.NotNull(dbRun);
        Assert.Equal(initialConvId, dbRun.ConversationId);
        Assert.Equal(initialRunId, dbRun.Id);
    }

    [Fact]
    public async Task Test5_MissedEventsAreRecovered_AfterDisconnect()
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChatRepository>();

        var conversation = await chatService.CreateConversationAsync();
        var request = new PostMessageRequest(
            UserMessageId: "msg_missed_test",
            Content: "Missed Events Recovery",
            DelayMs: 0,
            TargetChunkCount: 8
        );

        var run = await chatService.StartRunAsync(conversation.Id, request);

        // Wait for generation to finish completely in background
        for (int i = 0; i < 50; i++)
        {
            using var pollScope = _scopeFactory.CreateScope();
            var pollRepo = pollScope.ServiceProvider.GetRequiredService<IChatRepository>();
            var cur = await pollRepo.GetRunAsync(run.Id);
            if (cur?.Status == RunStatus.Completed) break;
            await Task.Delay(20);
        }

        // Client reconnects with cursor = 3 (simulating it received 1, 2, 3 before disconnect)
        var recoveredEvents = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamRunEventsAsync(run.Id, afterCursor: 3))
        {
            recoveredEvents.Add(evt);
        }

        // Total 8 text + 1 done = 9 events. After cursor 3: sequences 4, 5, 6, 7, 8, 9 = 6 events
        Assert.Equal(6, recoveredEvents.Count);
        Assert.Equal(4, recoveredEvents[0].Sequence);
        Assert.Equal(9, recoveredEvents.Last().Sequence);
        Assert.Equal(ChatEventType.Done, recoveredEvents.Last().Type);
    }

    [Fact]
    public async Task Test6_InvalidOrStaleCursor_ReturnsConflict()
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChatRepository>();
        var controller = new RunsController(chatService);

        var conv = await repo.CreateConversationAsync(new Conversation { Id = "conv_c1", CreatedAt = DateTimeOffset.UtcNow });
        var run = await repo.CreateRunAsync(new Run
        {
            Id = "run_c1",
            ConversationId = conv.Id,
            UserMessageId = "msg_c1",
            Status = RunStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await repo.AddEventAsync(new ChatEvent
        {
            Id = "evt_c1",
            RunId = run.Id,
            Sequence = 1,
            Type = ChatEventType.Done,
            Text = "",
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Negative cursor -> 409 Conflict
        var negResult = await controller.GetEvents(run.Id, after: -1);
        Assert.IsType<ConflictObjectResult>(negResult.Result);

        // Stale cursor (exceeds max sequence 1) -> 409 Conflict
        var staleResult = await controller.GetEvents(run.Id, after: 999);
        Assert.IsType<ConflictObjectResult>(staleResult.Result);
    }

    [Fact]
    public async Task Test7_ServiceRestart_TransitionsInProgressRunsToInterrupted()
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChatRepository>();

        var conv = await repo.CreateConversationAsync(new Conversation { Id = "conv_restart", CreatedAt = DateTimeOffset.UtcNow });
        var run = await repo.CreateRunAsync(new Run
        {
            Id = "run_restart",
            ConversationId = conv.Id,
            UserMessageId = "msg_restart",
            Status = RunStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // 3 partial events were saved before the service restarted
        for (int i = 1; i <= 3; i++)
        {
            await repo.AddEventAsync(new ChatEvent
            {
                Id = $"evt_rst_{i}",
                RunId = run.Id,
                Sequence = i,
                Type = ChatEventType.Text,
                Text = $"part_{i} ",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        // Simulate service restart startup hook
        var interruptedCount = await repo.HandleInterruptedRunsOnStartupAsync();
        Assert.Equal(1, interruptedCount);

        // Verify run is now Interrupted
        var updatedRun = await repo.GetRunAsync(run.Id);
        Assert.NotNull(updatedRun);
        Assert.Equal(RunStatus.Interrupted, updatedRun.Status);
        Assert.NotNull(updatedRun.CompletedAt);

        // Verify interrupted event was appended with sequence 4
        var events = await repo.GetEventsAsync(run.Id, afterSequence: 0);
        Assert.Equal(4, events.Count);
        Assert.Equal(4, events.Last().Sequence);
        Assert.Equal(ChatEventType.Error, events.Last().Type);
        Assert.Contains("service restart", events.Last().Text);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        try
        {
            if (File.Exists(_dbName))
            {
                File.Delete(_dbName);
            }
        }
        catch {}
    }
}
