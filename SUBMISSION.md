# Product Engineering Challenge Submission

## Candidate

- **Name:** Arockia Vincia
- **Email:** candidate@example.com
- **GitHub:** https://github.com/ArockiaVincia/ResumableChat
- **Selected problem:** Problem 1 — Resumable Realtime Conversation
- **Demo video:** [Link to 3–5 minute Loom, YouTube, or Google Drive demo video]

---

## Run the project

### Prerequisites
- [.NET 8 or .NET 10 SDK](https://dotnet.microsoft.com/download)
- Modern web browser (Chrome, Edge, Firefox, Safari)

No external database, Redis instance, or paid API keys are required. SQLite and all dependencies are initialized automatically.

### Exact Run Command
From the repository root:

```powershell
dotnet run --project src/ResumableChat.Api/ResumableChat.Api.csproj --urls "http://localhost:5000"
```

Once running, open your browser and navigate to:
```text
http://localhost:5000
```
(The ASP.NET Core server automatically serves the functional React client from `src/ResumableChat.Client/index.html`.)

### How to Trigger the Scenarios in the UI

1. **Successful Ordered Streaming (AC1)**:
   - Enter a message (or keep the default) and click **Send Message**.
   - Observe connection state: `Connecting` → `Connected` → `Completed`.
   - Observe words streaming progressively into the chat box with cursor advancing from `1` to `33`.

2. **Interruption & Reconnection from Cursor (AC2, AC3)**:
   - Click **Send Message**.
   - While words are streaming (e.g., around sequence 5–8), click **Simulate Disconnect (Drop Connection)**.
   - Observe connection state switch to `Reconnecting`.
   - The server generator continues producing events in the background.
   - The client reconnects automatically using `?after={cursor}`.
   - All missed events are seamlessly replayed with **zero missing events** and **zero duplicate events**, finishing in `Completed`.

3. **Generator Failure Recovery (AC5)**:
   - Check the **Simulate Failure after 5 chunks** checkbox.
   - Click **Send Message**.
   - The stream produces 5 events and then receives an explicit `error` event: `[ERROR: Simulated generator failure after emitting 5 events.]`.
   - The run state transitions to `Failed`, and durable partial history is preserved.

---

## Run the tests

Run the complete xUnit test suite (16 tests, 0 failures, ~2 seconds run time):

```powershell
dotnet test ResumableChat.sln
```

All tests are deterministic, run locally in memory/temp SQLite, and do not call paid APIs or use arbitrary long sleeps.

---

## Acceptance scenarios and verification

### Completed Acceptance Scenarios

- **AC1: Ordered live stream**: Server emits strictly sequential events (`1, 2, 3...`) backed by the SQLite unique index `IX_Events_RunId_Sequence`. Client displays events in order and reaches `Completed`.
- **AC2: Missed-event recovery**: Client sends `?after={lastProcessedSequence}`. The server queries SQLite for `Sequence > cursor` and replays every missed event without gaps.
- **AC3: Replay/live overlap**: The server suppresses live events with `sequence <= lastEmittedSequence` during replay-to-live channel handover. The client also implements sequence-based deduplication (`sequence <= lastProcessedSequence`), preventing duplicate logical display.
- **AC4: Service restart**: On startup, `repo.HandleInterruptedRunsOnStartupAsync()` transitions any dangling `running` runs to `interrupted`, records `CompletedAt`, and appends an interruption error event. Persisted history survives, and reconnecting clients receive an explicit terminal state.
- **AC5: Generation failure**: When the generator throws an exception, the server emits an `error` event, persists it, and marks the run `failed`. A failed run can never become completed.
- **AC6: Unknown or stale cursor**: Requesting `after < 0` or `after > maxSequence` returns `HTTP 409 Conflict` with an explanatory error payload rather than silently omitting data.

### Verification Benchmark

Run the repeatable verification benchmark:

```powershell
dotnet test --filter "Category=Benchmark" --logger "console;verbosity=normal"
```

Or execute the convenience runner script:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-benchmark.ps1
```

### Observed Benchmark Result

```text
=== Resumable Conversation Verification Benchmark ===

Run ID: 53049d655ed44ca98daa4b4e755a88c0
Conversation ID: e1f00205314a447695249490ef912dfa

Expected events: 33
Events received before interruption: 7
Reconnect cursor: 7
Events recovered after reconnect: 26
Total events received: 33

Missing events: 0
Duplicate events: 0
Out-of-order events: 0

Final run state: completed

RESULT: PASS
======================================================
```

- **Observed Counts**: 33 expected (32 text chunks + 1 done chunk), 7 received before interruption, 26 recovered after reconnect with `after=7`.
- **Integrity**: 0 missing, 0 duplicates, 0 out-of-order sequences.
- **Terminal State**: `completed`. Exactly 1 run created and preserved across reconnection.

### Failure / Recovery Scenario in Video
The demo video shows the user initiating a generation, clicking **Simulate Disconnect** at chunk 7, watching the client enter `Reconnecting`, and automatically reconnecting with `after=7`. The server replays events 8..33, and the client displays the complete, seamless response with zero duplicate words.

---

## Architecture and data flow

```text
               +-------------------------------------------+
               |        React Client (Browser)             |
               |  - Tracks lastProcessedSequence (cursor)  |
               |  - Deduplicates incoming sequence <= seq  |
               |  - Manages auto-reconnect backoff         |
               +--------------------+----------------------+
                                    |
                    HTTP POST (Send)| GET /stream?after={cursor} (SSE)
                                    v
               +--------------------+----------------------+
               |          ASP.NET Core Web API             |
               |  - RunsController / ConversationsController|
               +--------------------+----------------------+
                                    |
                                    v
               +--------------------+----------------------+
               |               ChatService                 |
               |  - Orchestrates run lifecycle & cursor    |
               |  - Bridges SQLite replay + live channel   |
               +----------+-----------------------+--------+
                          |                       |
               Persists   |                       | Publishes live
               events     v                       v
            +-------------+-----+      +----------+----------+
            |  ChatRepository   |      |   RunBroadcaster    |
            |  (SQLite DB)      |      | (In-Memory Channels)|
            |  DURABLE HISTORY  |      |   TRANSIENT LIVE    |
            +-------------------+      +---------------------+
```

### Data Flow & Responsibilities
1. **Durable Event History (SQLite)**: SQLite is the sole source of truth. Every chunk emitted by `FakeResponseGenerator` is written to SQLite before/as it is broadcast. Sequences are strictly monotonic (`1, 2, 3...`) with a unique database index `(RunId, Sequence)`.
2. **Transient Live Broadcaster (`RunBroadcaster`)**: Uses thread-safe unbounded channels (`System.Threading.Channels.Channel<ChatEvent>`) for real-time delivery to connected clients.
3. **Replay-to-Live Handover**: When a client connects or reconnects with `after={cursor}`:
   - Subscribes to the live channel first to avoid dropping concurrent events.
   - Fetches and yields all persisted events from SQLite where `Sequence > cursor`.
   - Bridges to live channel events, discarding any event where `Sequence <= lastEmittedSequence`.
   - Terminates cleanly upon `done` or `error`.

---

## Technology choices

- **Backend**: **C# / .NET / ASP.NET Core**
  - High performance, first-class asynchronous streams (`IAsyncEnumerable<T>`), and native `System.Threading.Channels` for robust pub-sub streaming.
- **Persistence**: **SQLite + Entity Framework Core**
  - Zero-configuration local database that survives service process restarts. Unique constraints guarantee ordering integrity.
- **Transport**: **Server-Sent Events (SSE)**
  - Unidirectional streaming over standard HTTP is the optimal fit for LLM chat generation.
  - Browser-native `EventSource` with simple reconnection headers (`?after={cursor}`) avoids the stateful connection overhead of WebSockets or SignalR.
- **Frontend**: **React 18**
  - Standalone SPA served directly by ASP.NET Core with zero build/npm installation overhead for the reviewer, while maintaining clean component state separation.

---

## Important decisions

1. **Server Owns Ordering**:
   The client never calculates or dictates event sequence numbers. Monotonically increasing sequences are generated by the server and enforced by a unique SQLite index `IX_Events_RunId_Sequence`.
2. **Cursor Representation (`lastProcessedSequence`)**:
   The cursor is the integer sequence number of the last successfully processed event. Reconnecting with `after=7` explicitly means *"send everything after sequence 7"*. Requesting an invalid or unavailable cursor (`after < 0` or `after > maxSequence`) returns an explicit `HTTP 409 Conflict`.
3. **Dual-Layer Deduplication**:
   - *Server-side*: `StreamRunEventsAsync` tracks `lastEmittedSequence` during SQLite replay and filters out channel events `sequence <= lastEmittedSequence`.
   - *Client-side*: The client ignores any event where `sequence <= lastProcessedSequence`.
   - Events are deduplicated strictly by server sequence ID, never by raw text.
4. **Honest Service Restart Semantics**:
   Rather than pretending an in-progress generator continued during a crash or restart, the server detects orphaned `running` runs on startup and transitions them to `interrupted`, recording the timestamp and appending an error event. All historical events up to the interruption survive and remain inspectable.

---

## Assumptions and limitations

- **Single-Node Deployment**: The prototype is designed for single-node execution. The in-memory broadcaster is local to the process.
- **No Generator Resumption on Restart**: On service crash/restart, in-progress runs transition to `interrupted`. Completed runs remain fully readable.
- **Fake Generator**: Uses a deterministic fake token generator designed for automated tests rather than a paid LLM API.

---

## Production and scale

If scaling this prototype for production:
1. **Distributed Pub/Sub**: Replace the in-process `RunBroadcaster` with Redis Streams, RabbitMQ, or Kafka to allow clients to reconnect to any instance behind a load balancer.
2. **Durable Database**: Migrate from SQLite to PostgreSQL with partitioned tables for high-throughput event logging.
3. **Event Compaction & Retention Window**: Implement a configurable event retention policy (e.g., retain raw chunks for 7 days, after which only the aggregated message is retained). If a client reconnects with an expired cursor, return an explicit `410 Gone` with instructions to fetch the full message.
4. **Authentication & Authorization**: Add JWT/Bearer token authentication and ensure runs are scoped to authenticated users.

---

## AI usage

AI tools (Anthropic Claude / Google Gemini) were utilized as coding pair assistants during the challenge to generate initial boilerplate, refine test cases, and draft documentation. All architecture, protocol design decisions, race condition mitigations, test implementations, and code reviews were reviewed, verified, and executed locally by the candidate.

---

## Credibility note

- **Product / System**: High-concurrency realtime messaging & workflow engine.
- **Problem Solved**: Ensured reliable, at-least-once message delivery and session resumption across intermittent mobile networks without duplicating transactional payloads.
- **Personal Contribution**: Designed the event sequencer and client cursor reconciliation layer using sequence checkpointing and state reconciliation.
- **Scale / Complexity**: Scaled to tens of thousands of concurrent active connections while maintaining sub-50ms message delivery latency.
- **Difficult Engineering Decision**: Chose an explicit append-only event log with monotonically increasing sequence IDs over client-generated timestamps to eliminate client clock-skew anomalies.
