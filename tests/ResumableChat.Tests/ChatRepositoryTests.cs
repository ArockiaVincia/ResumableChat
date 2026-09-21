using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ResumableChat.Api.Data;
using ResumableChat.Api.Data.Entities;
using ResumableChat.Api.Repositories;

namespace ResumableChat.Tests;

public class ChatRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ChatDbContext _context;
    private readonly ChatRepository _repository;

    public ChatRepositoryTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ChatDbContext(options);
        _context.Database.EnsureCreated();

        _repository = new ChatRepository(_context);
    }

    [Fact]
    public async Task CanCreateAndRetrieveConversationAndRun()
    {
        var conversation = new Conversation
        {
            Id = "conv_test_1",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _repository.CreateConversationAsync(conversation);

        var retrievedConv = await _repository.GetConversationAsync("conv_test_1");
        Assert.NotNull(retrievedConv);
        Assert.Equal("conv_test_1", retrievedConv.Id);

        var run = new Run
        {
            Id = "run_test_1",
            ConversationId = conversation.Id,
            UserMessageId = "msg_1",
            Status = RunStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _repository.CreateRunAsync(run);

        var retrievedRun = await _repository.GetRunAsync("run_test_1");
        Assert.NotNull(retrievedRun);
        Assert.Equal(RunStatus.Running, retrievedRun.Status);
    }

    [Fact]
    public async Task CanPersistAndRetrieveEventsAfterCursor()
    {
        var conversation = new Conversation { Id = "conv_2", CreatedAt = DateTimeOffset.UtcNow };
        await _repository.CreateConversationAsync(conversation);

        var run = new Run
        {
            Id = "run_2",
            ConversationId = conversation.Id,
            UserMessageId = "msg_2",
            Status = RunStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _repository.CreateRunAsync(run);

        for (int i = 1; i <= 5; i++)
        {
            await _repository.AddEventAsync(new ChatEvent
            {
                Id = $"evt_{i}",
                RunId = run.Id,
                Sequence = i,
                Type = ChatEventType.Text,
                Text = $"chunk_{i}",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        var allEvents = await _repository.GetEventsAsync(run.Id, afterSequence: 0);
        Assert.Equal(5, allEvents.Count);

        var eventsAfter3 = await _repository.GetEventsAsync(run.Id, afterSequence: 3);
        Assert.Equal(2, eventsAfter3.Count);
        Assert.Equal(4, eventsAfter3[0].Sequence);
        Assert.Equal(5, eventsAfter3[1].Sequence);
    }

    [Fact]
    public async Task EnforcesUniqueRunIdAndSequence()
    {
        var conversation = new Conversation { Id = "conv_3", CreatedAt = DateTimeOffset.UtcNow };
        await _repository.CreateConversationAsync(conversation);

        var run = new Run
        {
            Id = "run_3",
            ConversationId = conversation.Id,
            UserMessageId = "msg_3",
            Status = RunStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _repository.CreateRunAsync(run);

        await _repository.AddEventAsync(new ChatEvent
        {
            Id = "evt_a",
            RunId = run.Id,
            Sequence = 1,
            Type = ChatEventType.Text,
            Text = "first",
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Inserting another event with the same RunId and Sequence must fail
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await _repository.AddEventAsync(new ChatEvent
            {
                Id = "evt_b",
                RunId = run.Id,
                Sequence = 1,
                Type = ChatEventType.Text,
                Text = "duplicate sequence",
                CreatedAt = DateTimeOffset.UtcNow
            });
        });
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
