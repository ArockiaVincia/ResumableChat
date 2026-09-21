namespace ResumableChat.Api.Services;

public record GeneratorChunk(string Text, bool IsFinal = false);

public interface IFakeResponseGenerator
{
    IAsyncEnumerable<GeneratorChunk> GenerateResponseAsync(
        string prompt,
        int? failAfterCount = null,
        int delayMs = 0,
        int? targetChunkCount = null,
        CancellationToken cancellationToken = default);
}
