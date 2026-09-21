using System.Threading.Channels;
using ResumableChat.Api.Data.Entities;

namespace ResumableChat.Api.Services;

public interface IRunSubscription : IAsyncDisposable
{
    ChannelReader<ChatEvent> Reader { get; }
}

public interface IRunBroadcaster
{
    ValueTask PublishEventAsync(string runId, ChatEvent chatEvent);
    IRunSubscription Subscribe(string runId);
}
