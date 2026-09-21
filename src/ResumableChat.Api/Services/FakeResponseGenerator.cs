using System.Runtime.CompilerServices;

namespace ResumableChat.Api.Services;

public class SimulatedGeneratorException : Exception
{
    public SimulatedGeneratorException(string message) : base(message) { }
}

public class FakeResponseGenerator : IFakeResponseGenerator
{
    private static readonly string[] DefaultTokens = new[]
    {
        "The", "persistent", "conversational", "companion", "streams", "replies", "so", "the",
        "user", "can", "read", "them", "as", "they", "are", "generated.",
        "Connections", "can", "drop,", "browsers", "can", "sleep,", "mobile", "networks",
        "can", "change,", "and", "the", "service", "can", "restart", "smoothly.",
        "Every", "event", "is", "strictly", "ordered", "and", "durable."
    };

    public async IAsyncEnumerable<GeneratorChunk> GenerateResponseAsync(
        string prompt,
        int? failAfterCount = null,
        int delayMs = 0,
        int? targetChunkCount = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var count = targetChunkCount ?? (DefaultTokens.Length);
        
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
            else
            {
                await Task.Yield();
            }

            var token = DefaultTokens[i % DefaultTokens.Length];
            var isFinal = (i == count - 1);
            
            // Append a space if not final
            var text = isFinal ? token : token + " ";
            yield return new GeneratorChunk(text, isFinal);

            var emittedCount = i + 1;
            if (failAfterCount.HasValue && emittedCount >= failAfterCount.Value)
            {
                throw new SimulatedGeneratorException($"Simulated generator failure after emitting {emittedCount} events.");
            }
        }
    }
}
