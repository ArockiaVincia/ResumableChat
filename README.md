# Resumable Realtime Conversation

**Caygnus Product Engineering Challenge — Problem 1 Solution**

A robust, resumable conversational streaming service and client that guarantees deterministic event ordering, session recovery across interruptions, and zero duplicate or missing messages.

---

## Quick Start

### 1. Prerequisites
- [.NET 8 or .NET 10 SDK](https://dotnet.microsoft.com/download)
- A modern web browser (Chrome, Edge, Firefox, Safari)

### 2. Run the Application
From the repository root:

```powershell
dotnet run --project src/ResumableChat.Api/ResumableChat.Api.csproj --urls "http://localhost:5000"
```

Open your browser at:
```text
http://localhost:5000
```
The ASP.NET Core server automatically initializes SQLite and serves the React client UI.

---

## Running Tests

Run the complete deterministic test suite:

```powershell
dotnet test ResumableChat.sln
```

- **34 passing tests**, 0 failures.
- No external paid APIs or flaky long sleeps.

---

## Running the Verification Benchmark

Execute the repeatable verification benchmark:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-benchmark.ps1
```
or
```powershell
dotnet test --filter "Category=Benchmark" --logger "console;verbosity=normal"
```

**Benchmark Result:**
- **Expected Events**: 33
- **Events Before Interruption**: 7
- **Reconnect Cursor**: `after=7`
- **Events Recovered After Reconnect**: 26
- **Missing Events**: 0
- **Duplicate Events**: 0
- **Out-of-Order Events**: 0
- **Final Run State**: `completed`
- **Result**: PASS

---

## Documentation

- **[SUBMISSION.md](SUBMISSION.md)**: Full challenge submission document covering architecture, technology choices, trade-offs, and credibility notes.
- **[docs/VERIFICATION.md](docs/VERIFICATION.md)**: Benchmark specification and metrics.
- **[docs/DEMO_SCRIPT.md](docs/DEMO_SCRIPT.md)**: Timed 3–5 minute narrated recording walkthrough.
