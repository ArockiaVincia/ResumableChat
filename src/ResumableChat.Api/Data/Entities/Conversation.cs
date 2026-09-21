namespace ResumableChat.Api.Data.Entities;

public class Conversation
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Run> Runs { get; set; } = new();
}
