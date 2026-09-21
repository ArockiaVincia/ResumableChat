namespace ResumableChat.Api.Data.Entities;

public static class RunStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";
}

public class Run
{
    public string Id { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string UserMessageId { get; set; } = string.Empty;
    public string Status { get; set; } = RunStatus.Running;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public Conversation? Conversation { get; set; }
    public List<ChatEvent> Events { get; set; } = new();
}
