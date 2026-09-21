namespace ResumableChat.Api.Data.Entities;

public static class ChatEventType
{
    public const string Text = "text";
    public const string Done = "done";
    public const string Error = "error";
}

public class ChatEvent
{
    public string Id { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string Type { get; set; } = ChatEventType.Text;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Run? Run { get; set; }
}
