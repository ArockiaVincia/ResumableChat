using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ResumableChat.Api.Services;

public class SimulatedGeneratorException : Exception
{
    public SimulatedGeneratorException(string message) : base(message) { }
}

public class FakeResponseGenerator : IFakeResponseGenerator
{
    public const string FallbackResponse =
        "In modern software engineering and systems development, addressing this effectively involves assessing the core architectural requirements, establishing clear domain boundaries, and applying proven design principles to ensure maintainability, reliability, and high performance.";

    private static readonly (string Pattern, string Response)[] TopicMappings = new[]
    {
        (
            @"\breact(?:\.js|js)?\b",
            "React is a JavaScript library used to build user interfaces. It uses reusable components, props, state, and hooks to create interactive web applications."
        ),
        (
            @"(?:\bdotnet|\basp\.net|(?<!\w)\.net|\bdot\s*net|\bdot\s*core)(?:\s*core)?\b",
            ".NET Core is a cross-platform development framework from Microsoft used to build web applications, Web APIs, background services, and other applications. Modern .NET versions such as .NET 8 continue this cross-platform platform."
        ),
        (
            @"(?:\b(?:csharp|c-sharp)\b|(?<!\w)c#(?!\w))",
            "C# is a modern, object-oriented, type-safe programming language developed by Microsoft. It runs on the .NET runtime and is used to build enterprise backends, cloud applications, and web services."
        ),
        (
            @"\b(?:web\s*api|webapi)\b",
            "ASP.NET Core Web API is a framework for building HTTP services that allow applications such as web or mobile clients to communicate with backend functionality through endpoints."
        ),
        (
            @"\b(?:entity\s*framework(?:\s*core)?|ef\s*core|efcore)\b",
            "Entity Framework Core is an object-relational mapper for .NET. It allows developers to work with databases using C# objects and LINQ instead of writing every database operation manually."
        ),
        (
            @"\b(?:dependency\s*injection)\b|\bdi\b",
            "Dependency Injection is a design pattern where required dependencies are provided to a class instead of the class creating them itself. In ASP.NET Core, the built-in dependency injection container manages service registration and lifetime."
        ),
        (
            @"\b(?:rest\s*api|restful(?:\s*api)?|rest)\b",
            "A REST API is an architectural style for network communication based on stateless HTTP requests, standard HTTP verbs like GET, POST, PUT, and DELETE, and standard status codes for resource management."
        ),
        (
            @"\b(?:sql\s*server|mssql|sql)\b",
            "Microsoft SQL Server is an enterprise relational database management system that stores and retrieves data requested by applications, providing ACID compliance, high performance, relational constraints, and security."
        ),
        (
            @"\b(?:javascript|js)\b",
            "JavaScript is a high-level programming language that powers interactive user interfaces in web browsers and runs on server-side runtimes like Node.js, featuring an event-driven asynchronous execution model."
        ),
        (
            @"\b(?:jwt|json\s*web\s*token)\b",
            "JSON Web Token (JWT) is an open standard compact, URL-safe token format used to securely transmit verifiable claims between parties, commonly used for stateless authentication and authorization in Web APIs."
        ),
        (
            @"\b(?:linq)\b",
            "Language Integrated Query (LINQ) is a set of features in C# that introduces native query capabilities directly into the language, allowing developers to query collections, databases, and XML with type safety."
        )
    };

    public static string GenerateGenericResponse(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Hello! I am FlowChat, your conversational AI assistant. You can ask me questions on a wide variety of topics, and I will stream responses directly to you.";
        }

        var cleaned = prompt.Trim().TrimEnd('?').Trim();
        return $"That is a thoughtful question regarding \"{cleaned}\". In modern software engineering and systems development, addressing this effectively involves assessing the core architectural requirements, establishing clear domain boundaries, and applying proven design principles to ensure maintainability, reliability, and high performance.";
    }

    public static (string Answer, bool IsTopicMatched) ResolveAnswer(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return ("Hi! How can I help you today?", true);
        }

        var trimmed = prompt.Trim();

        // 1. Greeting check ("hi", "hello", "hey", etc.)
        if (Regex.IsMatch(trimmed, @"^\s*(?:hi|hello|hey|greetings|good\s+(?:morning|afternoon|evening))\b(?:\s+(?:there|friend|assistant|bot))?[\s!.,?]*$", RegexOptions.IgnoreCase))
        {
            return ("Hi! How can I help you today?", true);
        }

        // 2. General help check ("okay how you will help me out", "how can you help me", "what can you help with", etc.)
        if (Regex.IsMatch(trimmed, @"(how\s+(?:can\s+you|will\s+you|you\s+will)\s+help|what\s+can\s+you\s+help\s+with|help\s+me\s+out|how\s+do\s+you\s+help|how\s+can\s+i\s+use\s+you)", RegexOptions.IgnoreCase))
        {
            return ("I can help explain technical concepts, debug code, discuss .NET and React development, work through programming problems, and answer general questions.", true);
        }

        // 3. React latest version check (must precede general React match)
        if (Regex.IsMatch(trimmed, @"\b(?:latest\s+version|current\s+version|version)\s+of\s+react|\breact(?:\.js|js)?\s+version\b", RegexOptions.IgnoreCase))
        {
            return ("I don't have live package-version lookup enabled in this demo, so I can't reliably confirm the latest React version.", true);
        }

        // 4. React UI development check (must precede general React match)
        if (Regex.IsMatch(trimmed, @"\breact(?:\.js|js)?\b.*\bui\s+development\b|\bui\s+development\b.*\breact(?:\.js|js)?\b", RegexOptions.IgnoreCase))
        {
            return ("I can support React UI development by guiding you through creating reusable components, managing state with hooks such as useState and useEffect, handling props and forms, structuring component hierarchies, integrating REST APIs, and building responsive, interactive user interfaces.", true);
        }

        // 5. Practical dependency injection creation check (must precede general DI match)
        if (Regex.IsMatch(trimmed, @"(?:how\s+(?:to\s+)?(?:create|implement|configure|use|setup|set\s*up)|create|implement)\s+(?:a\s+)?(?:dependency\s*injection|di)\b", RegexOptions.IgnoreCase))
        {
            return ("To create dependency injection in ASP.NET Core: 1. Define your service interface and implementation class. 2. Register the service in Program.cs using builder.Services (such as AddScoped, AddTransient, or AddSingleton). 3. Inject the dependency via constructor injection into your controller or consuming service.", true);
        }

        // 6. Technical topics and combined questions
        var matchedAnswers = new List<(int Index, string Response)>();

        foreach (var (pattern, response) in TopicMappings)
        {
            var match = Regex.Match(trimmed, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                matchedAnswers.Add((match.Index, response));
            }
        }

        if (matchedAnswers.Count > 0)
        {
            var orderedResponses = matchedAnswers
                .OrderBy(m => m.Index)
                .Select(m => m.Response)
                .Distinct();

            var combinedAnswer = string.Join(" ", orderedResponses);
            return (combinedAnswer, true);
        }

        // 6. Unknown / general question fallback
        var cleaned = trimmed.TrimEnd('?').Trim();
        var fallback = $"I don't have a specific built-in answer for \"{cleaned}\" in this demo, but I can answer questions about React, .NET Core, C#, Web API, Dependency Injection, and EF Core.";
        return (fallback, false);
    }

    public async IAsyncEnumerable<GeneratorChunk> GenerateResponseAsync(
        string prompt,
        int? failAfterCount = null,
        int delayMs = 0,
        int? targetChunkCount = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (answer, isTopicMatched) = ResolveAnswer(prompt);
        var tokens = answer.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            tokens = new[] { "OK" };
        }

        // Use targetChunkCount if explicitly provided (e.g. benchmark or specific test), otherwise natural token length
        int count = targetChunkCount ?? tokens.Length;

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

            var token = tokens[i % tokens.Length];
            var isFinal = (i == count - 1);

            // Append space if not final token
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
