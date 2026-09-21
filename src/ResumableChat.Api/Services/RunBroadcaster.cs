using System.Collections.Concurrent;
using System.Threading.Channels;
using ResumableChat.Api.Data.Entities;

namespace ResumableChat.Api.Services;

public class RunSubscription : IRunSubscription
{
    private readonly string _runId;
    private readonly Guid _subscriberId;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<ChatEvent>>> _subscribers;
    private readonly Channel<ChatEvent> _channel;

    public ChannelReader<ChatEvent> Reader => _channel.Reader;

    public RunSubscription(
        string runId,
        Guid subscriberId,
        ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<ChatEvent>>> subscribers,
        Channel<ChatEvent> channel)
    {
        _runId = runId;
        _subscriberId = subscriberId;
        _subscribers = subscribers;
        _channel = channel;
    }

    public ValueTask DisposeAsync()
    {
        if (_subscribers.TryGetValue(_runId, out var runSubscribers))
        {
            if (runSubscribers.TryRemove(_subscriberId, out _))
            {
                if (runSubscribers.IsEmpty)
                {
                    _subscribers.TryRemove(_runId, out _);
                }
            }
        }
        return ValueTask.CompletedTask;
    }
}

public class RunBroadcaster : IRunBroadcaster
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<ChatEvent>>> _subscribers = new();
    private readonly ConcurrentDictionary<string, bool> _completedRuns = new();

    public async ValueTask PublishEventAsync(string runId, ChatEvent chatEvent)
    {
        if (chatEvent.Type == ChatEventType.Done || chatEvent.Type == ChatEventType.Error)
        {
            _completedRuns[runId] = true;
        }

        if (_subscribers.TryGetValue(runId, out var subscribers))
        {
            foreach (var kvp in subscribers)
            {
                kvp.Value.Writer.TryWrite(chatEvent);
                if (chatEvent.Type == ChatEventType.Done || chatEvent.Type == ChatEventType.Error)
                {
                    kvp.Value.Writer.TryComplete();
                }
            }
        }
        await ValueTask.CompletedTask;
    }

    public IRunSubscription Subscribe(string runId)
    {
        var subscriberId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<ChatEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // Register synchronously so no events emitted after this call are missed
        var runSubscribers = _subscribers.GetOrAdd(runId, _ => new ConcurrentDictionary<Guid, Channel<ChatEvent>>());
        runSubscribers[subscriberId] = channel;

        if (_completedRuns.TryGetValue(runId, out var completed) && completed)
        {
            channel.Writer.TryComplete();
        }

        return new RunSubscription(runId, subscriberId, _subscribers, channel);
    }
}
