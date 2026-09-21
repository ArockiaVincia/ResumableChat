using ResumableChat.Api.Services;

namespace ResumableChat.Tests;

public class FakeResponseGeneratorTests
{
    [Fact]
    public async Task GeneratesExpectedNumberOfChunksDeterministically()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<GeneratorChunk>();

        await foreach (var chunk in generator.GenerateResponseAsync(prompt: "Hello", targetChunkCount: 30, delayMs: 0))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(30, chunks.Count);
        Assert.True(chunks.Last().IsFinal);
        Assert.False(chunks.First().IsFinal);
        Assert.NotEmpty(chunks[0].Text);
    }

    [Fact]
    public async Task ThrowsSimulatedGeneratorExceptionWhenFailAfterCountReached()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<GeneratorChunk>();

        var ex = await Assert.ThrowsAsync<SimulatedGeneratorException>(async () =>
        {
            await foreach (var chunk in generator.GenerateResponseAsync(
                prompt: "Test Failure",
                failAfterCount: 5,
                delayMs: 0,
                targetChunkCount: 30))
            {
                chunks.Add(chunk);
            }
        });

        Assert.Equal(5, chunks.Count);
        Assert.Contains("Simulated generator failure after emitting 5 events", ex.Message);
    }
}
