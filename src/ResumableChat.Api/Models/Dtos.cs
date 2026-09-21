namespace ResumableChat.Api.Models;

public record CreateConversationResponse(string Id, DateTimeOffset CreatedAt);

public record PostMessageRequest(
    string UserMessageId,
    string Content,
    int? FailAfterCount = null,
    int DelayMs = 0,
    int? TargetChunkCount = null
);

public record RunResponse(
    string Id,
    string ConversationId,
    string UserMessageId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt
);

public record RunDetailResponse(
    string Id,
    string ConversationId,
    string UserMessageId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int EventCount,
    List<ChatEventResponse> Events
);

public record ChatEventResponse(
    string Id,
    string RunId,
    long Sequence,
    string Type,
    string Text,
    DateTimeOffset CreatedAt
);
