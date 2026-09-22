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

    [Fact]
    public async Task ReactQuestion_ReturnsReactExplanation()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("What is React?", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("React is a JavaScript library used to build user interfaces", fullText);
    }

    [Fact]
    public async Task DotNetCoreQuestion_ReturnsDotNetCoreExplanation()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("What is .NET Core?", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains(".NET Core is a cross-platform development framework from Microsoft", fullText);
    }

    [Theory]
    [InlineData("what is react")]
    [InlineData("WHAT IS REACT")]
    [InlineData("Tell me about React")]
    public async Task CaseInsensitiveMatching_ReturnsExpectedExplanation(string prompt)
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync(prompt, delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("React is a JavaScript library used to build user interfaces", fullText);
    }

    [Fact]
    public async Task CombinedQuestion_ReactAndDotNetCore_ReturnsBothTopicsInOrder()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("What is React and .NET Core?", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("React is a JavaScript library", fullText);
        Assert.Contains(".NET Core is a cross-platform development framework", fullText);

        var reactIndex = fullText.IndexOf("React is a JavaScript library", StringComparison.Ordinal);
        var dotNetIndex = fullText.IndexOf(".NET Core is a cross-platform development framework", StringComparison.Ordinal);
        Assert.True(reactIndex < dotNetIndex, "React should appear before .NET Core as asked in prompt.");
    }

    [Fact]
    public async Task CombinedQuestion_CSharpAndDependencyInjection_ReturnsBothTopicsInOrder()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("What is C# and dependency injection?", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("C# is a modern, object-oriented, type-safe programming language", fullText);
        Assert.Contains("Dependency Injection is a design pattern", fullText);

        var csharpIndex = fullText.IndexOf("C# is a modern", StringComparison.Ordinal);
        var diIndex = fullText.IndexOf("Dependency Injection is a design pattern", StringComparison.Ordinal);
        Assert.True(csharpIndex < diIndex, "C# should appear before Dependency Injection as asked in prompt.");
    }

    [Fact]
    public async Task UnknownQuestion_ReturnsHonestFallback()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("Can you write a poem about quantum physics?", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("I don't have a specific built-in answer for", fullText);
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("hello")]
    [InlineData("hey")]
    public async Task Greeting_ReturnsNaturalShortResponse(string prompt)
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync(prompt, delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks).Trim();
        Assert.Equal("Hi! How can I help you today?", fullText);
    }

    [Fact]
    public async Task GeneralHelp_ReturnsHelpCapabilities()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("okay how you will help me out", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("I can help explain technical concepts", fullText);
        Assert.Contains(".NET and React development", fullText);
    }

    [Fact]
    public async Task ReactUiDevelopment_ReturnsComponentAndHooksExplanation()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("list out how will you support for react js ui development", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("support React UI development", fullText);
        Assert.Contains("components", fullText);
        Assert.Contains("hooks", fullText);
    }

    [Fact]
    public async Task ReactLatestVersion_ReturnsNoLiveLookupDisclaimer()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("what is the latest version of react js", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("I don't have live package-version lookup enabled in this demo", fullText);
        Assert.DoesNotContain("React is a JavaScript library used to build user interfaces", fullText);
    }

    [Fact]
    public async Task FailureSimulationAfter5Chunks_WorksForTopicQuestion()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<GeneratorChunk>();

        var ex = await Assert.ThrowsAsync<SimulatedGeneratorException>(async () =>
        {
            await foreach (var chunk in generator.GenerateResponseAsync(
                prompt: "What is React?",
                failAfterCount: 5,
                delayMs: 0))
            {
                chunks.Add(chunk);
            }
        });

        Assert.Equal(5, chunks.Count);
        Assert.Contains("Simulated generator failure after emitting 5 events", ex.Message);
    }

    [Fact]
    public async Task HowToCreateDependencyInjection_ReturnsPracticalAspNetCoreExplanation()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("how to create dependency injection", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("service interface", fullText);
        Assert.Contains("Program.cs", fullText);
        Assert.Contains("constructor injection", fullText);
    }

    [Fact]
    public async Task WhatIsReactAndDotCore_ReturnsShortExplanationOfBoth()
    {
        var generator = new FakeResponseGenerator();
        var chunks = new List<string>();

        await foreach (var chunk in generator.GenerateResponseAsync("what is react and dot core", delayMs: 0))
        {
            chunks.Add(chunk.Text);
        }

        var fullText = string.Concat(chunks);
        Assert.Contains("React is a JavaScript library", fullText);
        Assert.Contains(".NET Core is a cross-platform development framework", fullText);
    }
}
