using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumableChat.Api.Data;
using ResumableChat.Api.Data.Entities;
using ResumableChat.Api.Models;
using ResumableChat.Api.Repositories;
using ResumableChat.Api.Services;

namespace ResumableChat.Tests;

public class ChatStreamingTests : IDisposable
{
    private readonly string _dbName;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public ChatStreamingTests()
    {
        _dbName = $"chat_test_{Guid.NewGuid():N}.db";

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
    public async Task EventsArePersistedWithIncreasingSequenceNumbersAndCompletedStatus()
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        var conversation = await chatService.CreateConversationAsync();
        var request = new PostMessageRequest(
            UserMessageId: "user_msg_1",
            Content: "Tell me a story",
            DelayMs: 0,
            TargetChunkCount: 10
        );

        var run = await chatService.StartRunAsync(conversation.Id, request);

        // Poll for completion
        for (int i = 0; i < 50; i++)
        {
            using var pollScope = _scopeFactory.CreateScope();
            var pollRepo = pollScope.ServiceProvider.GetRequiredService<IChatRepository>();
            var currentRun = await pollRepo.GetRunAsync(run.Id);
            if (currentRun?.Status == RunStatus.Completed)
            {
                break;
            }
            await Task.Delay(20);
        }

        using var verifyScope = _scopeFactory.CreateScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<IChatRepository>();
        var completedRun = await verifyRepo.GetRunAsync(run.Id);
        
        Assert.NotNull(completedRun);
        Assert.Equal(RunStatus.Completed, completedRun.Status);
        Assert.NotNull(completedRun.CompletedAt);

        var events = await verifyRepo.GetEventsAsync(run.Id, afterSequence: 0);
        // 10 text events + 1 done event = 11 events
        Assert.Equal(11, events.Count);

        for (int i = 0; i < events.Count; i++)
        {
            Assert.Equal(i + 1, events[i].Sequence);
        }

        Assert.Equal(ChatEventType.Done, events.Last().Type);
    }

    [Fact]
    public async Task FailedRunRemainsFailedAfterPartialEvents()
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        var conversation = await chatService.CreateConversationAsync();
        var request = new PostMessageRequest(
            UserMessageId: "user_msg_fail",
            Content: "Fail please",
            FailAfterCount: 4,
            DelayMs: 0,
            TargetChunkCount: 20
        );

        var run = await chatService.StartRunAsync(conversation.Id, request);

        // Wait for generation to encounter the failure
        for (int i = 0; i < 50; i++)
        {
            using var pollScope = _scopeFactory.CreateScope();
            var pollRepo = pollScope.ServiceProvider.GetRequiredService<IChatRepository>();
            var currentRun = await pollRepo.GetRunAsync(run.Id);
            if (currentRun?.Status == RunStatus.Failed)
            {
                break;
            }
            await Task.Delay(20);
        }

        using var verifyScope = _scopeFactory.CreateScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<IChatRepository>();
        var failedRun = await verifyRepo.GetRunAsync(run.Id);
        
        Assert.NotNull(failedRun);
        Assert.Equal(RunStatus.Failed, failedRun.Status);
        Assert.NotNull(failedRun.CompletedAt);

        var events = await verifyRepo.GetEventsAsync(run.Id, afterSequence: 0);
        // 4 text events + 1 error event = 5 events
        Assert.Equal(5, events.Count);
        Assert.Equal(ChatEventType.Error, events.Last().Type);
        Assert.Contains("Simulated generator failure after emitting 4 events", events.Last().Text);

        // Wait additional cycles to ensure it does NOT become completed later
        await Task.Delay(50);
        using var finalScope = _scopeFactory.CreateScope();
        var finalRepo = finalScope.ServiceProvider.GetRequiredService<IChatRepository>();
        var finalCheck = await finalRepo.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, finalCheck!.Status);
    }

    [Fact]
    public async Task StreamUsesPersistedEventInformationWithoutCreatingNewRun()
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        var conversation = await chatService.CreateConversationAsync();
        var request = new PostMessageRequest(
            UserMessageId: "user_msg_stream",
            Content: "Stream test",
            DelayMs: 0,
            TargetChunkCount: 5
        );

        var run = await chatService.StartRunAsync(conversation.Id, request);

        // Stream all events
        var streamedEvents = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamRunEventsAsync(run.Id, afterCursor: 0))
        {
            streamedEvents.Add(evt);
        }

        Assert.Equal(6, streamedEvents.Count); // 5 text + 1 done

        // Stream a second time on the same run with afterCursor = 3
        var replayEvents = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamRunEventsAsync(run.Id, afterCursor: 3))
        {
            replayEvents.Add(evt);
        }

        Assert.Equal(3, replayEvents.Count); // sequences 4, 5, 6
        Assert.Equal(4, replayEvents[0].Sequence);
        Assert.Equal(5, replayEvents[1].Sequence);
        Assert.Equal(6, replayEvents[2].Sequence);

        // Verify run still intact and only 1 run created
        using var verifyScope = _scopeFactory.CreateScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<IChatRepository>();
        var runDetails = await verifyRepo.GetRunAsync(run.Id);
        Assert.NotNull(runDetails);
        Assert.Equal(conversation.Id, runDetails.ConversationId);
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
        catch
        {
            // Best effort cleanup
        }
    }
}
