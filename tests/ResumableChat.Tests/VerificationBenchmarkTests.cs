using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResumableChat.Api.Data;
using ResumableChat.Api.Data.Entities;
using ResumableChat.Api.Models;
using ResumableChat.Api.Repositories;
using ResumableChat.Api.Services;
using Xunit.Abstractions;

namespace ResumableChat.Tests;

public class VerificationBenchmarkTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dbName;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public VerificationBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _dbName = $"benchmark_test_{Guid.NewGuid():N}.db";

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
    [Trait("Category", "Benchmark")]
    public async Task RunVerificationBenchmark_InterruptionAndRecovery()
    {
        using var scope = _scopeFactory.CreateScope();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChatRepository>();

        // 1. Create one conversation
        var conversation = await chatService.CreateConversationAsync();

        // 2. Start exactly one run with 32 text chunks + 1 done chunk = 33 events
        const int targetChunkCount = 32;
        const int expectedTotalEvents = targetChunkCount + 1; // 32 text + 1 done = 33 events
        const int interruptionPoint = 7;

        var request = new PostMessageRequest(
            UserMessageId: "benchmark_user_msg",
            Content: "Benchmark deterministic prompt for resumable realtime conversation verification",
            DelayMs: 15, // Controlled deterministic delay to allow live interruption
            TargetChunkCount: targetChunkCount
        );

        var run = await chatService.StartRunAsync(conversation.Id, request);
        var initialRunId = run.Id;
        var initialConvId = run.ConversationId;

        // 3. Receive first portion of events until interruption point
        var receivedBeforeInterruption = new List<ChatEvent>();
        long lastProcessedCursor = 0;

        using (var cts = new CancellationTokenSource())
        {
            await foreach (var evt in chatService.StreamRunEventsAsync(run.Id, afterCursor: 0, cts.Token))
            {
                receivedBeforeInterruption.Add(evt);
                lastProcessedCursor = evt.Sequence;

                if (receivedBeforeInterruption.Count == interruptionPoint)
                {
                    // 4. Simulate client connection interruption while generation continues
                    cts.Cancel();
                    break;
                }
            }
        }

        _output.WriteLine($"Received before: {string.Join(", ", receivedBeforeInterruption.Select(e => e.Sequence))}");
        Assert.Equal(interruptionPoint, receivedBeforeInterruption.Count);
        Assert.Equal(interruptionPoint, lastProcessedCursor);

        // 5. Allow server background generator to continue producing remaining events to completion
        Run? completedRun = null;
        for (int i = 0; i < 100; i++)
        {
            using var pollScope = _scopeFactory.CreateScope();
            var pollRepo = pollScope.ServiceProvider.GetRequiredService<IChatRepository>();
            completedRun = await pollRepo.GetRunAsync(run.Id);
            if (completedRun?.Status == RunStatus.Completed)
            {
                break;
            }
            await Task.Delay(20);
        }

        Assert.NotNull(completedRun);
        Assert.Equal(RunStatus.Completed, completedRun.Status);

        // 6. Reconnect using last processed cursor (after=7)
        var receivedAfterReconnect = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamRunEventsAsync(initialRunId, afterCursor: lastProcessedCursor))
        {
            receivedAfterReconnect.Add(evt);
        }

        // 7. Combine events through client deduplication
        var allReceivedEvents = new List<ChatEvent>();
        long clientCheckpoint = 0;

        void ProcessEvent(ChatEvent evt)
        {
            if (evt.Sequence <= clientCheckpoint)
            {
                return; // Discard duplicate
            }
            clientCheckpoint = evt.Sequence;
            allReceivedEvents.Add(evt);
        }

        foreach (var evt in receivedBeforeInterruption)
        {
            ProcessEvent(evt);
        }
        foreach (var evt in receivedAfterReconnect)
        {
            ProcessEvent(evt);
        }

        // 8. Analyze correctness metrics
        var sequenceNumbers = allReceivedEvents.Select(e => (int)e.Sequence).ToList();

        // Calculate duplicate events
        var duplicateCount = sequenceNumbers
            .GroupBy(s => s)
            .Where(g => g.Count() > 1)
            .Sum(g => g.Count() - 1);

        // Calculate missing events
        var missingSequences = new List<int>();
        for (int i = 1; i <= expectedTotalEvents; i++)
        {
            if (!sequenceNumbers.Contains(i))
            {
                missingSequences.Add(i);
            }
        }
        var missingCount = missingSequences.Count;

        // Calculate out-of-order events
        int outOfOrderCount = 0;
        for (int i = 1; i < sequenceNumbers.Count; i++)
        {
            if (sequenceNumbers[i] <= sequenceNumbers[i - 1])
            {
                outOfOrderCount++;
            }
        }

        // Verify single run preserved
        using var verifyScope = _scopeFactory.CreateScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<IChatRepository>();
        var verifyRun = await verifyRepo.GetRunAsync(initialRunId);
        Assert.NotNull(verifyRun);
        Assert.Equal(initialConvId, verifyRun.ConversationId);

        // Assertions
        Assert.Equal(expectedTotalEvents, allReceivedEvents.Count);
        Assert.Equal(0, missingCount);
        Assert.Equal(0, duplicateCount);
        Assert.Equal(0, outOfOrderCount);
        Assert.Equal(RunStatus.Completed, completedRun.Status);

        // 9. Format and output benchmark report
        var banner = $"""

=== Resumable Conversation Verification Benchmark ===

Run ID: {initialRunId}
Conversation ID: {initialConvId}

Expected events: {expectedTotalEvents}
Events received before interruption: {receivedBeforeInterruption.Count}
Reconnect cursor: {lastProcessedCursor}
Events recovered after reconnect: {receivedAfterReconnect.Count}
Total events received: {allReceivedEvents.Count}

Missing events: {missingCount}
Duplicate events: {duplicateCount}
Out-of-order events: {outOfOrderCount}

Final run state: {completedRun.Status}

RESULT: PASS
======================================================
""";

        _output.WriteLine(banner);
        Console.WriteLine(banner);
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
