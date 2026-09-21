# Demo Video Script (3–5 Minutes)

**Target Duration:** 3 to 5 minutes  
**Goal:** Clearly demonstrate the core requirements of Problem 1: Resumable Realtime Conversation.

---

## 0:00 – 0:45 | Part 1: Introduction & Architecture

**Spoken:**
> "Hi, I'm presenting my solution for Caygnus Product Engineering Challenge, Problem 1: Resumable Realtime Conversation.
> 
> The core challenge is: a chat assistant streams replies so users can read them progressively. But network connections drop, mobile devices sleep, and services restart. A reconnect must not restart the reply, repeat text, or silently skip content.
> 
> Here is the architecture:
> - **Frontend**: React client using browser-native Server-Sent Events (`EventSource`). It tracks the client cursor (`lastProcessedSequence`) and implements sequence-based deduplication.
> - **Backend**: ASP.NET Core Web API with C#.
> - **Service Layer**: `ChatService` orchestrates runs. It decouples **Durable History** in SQLite from **Transient Live Delivery** via an in-memory `System.Threading.Channels` broadcaster.
> - **Ordering**: The server owns ordering with strictly increasing sequence numbers, guaranteed unique in SQLite."

---

## 0:45 – 1:30 | Part 2: Normal Streaming (AC1)

**Action on screen:**
1. Show running API and navigate to `http://localhost:5000`.
2. Click **Send Message** with the default prompt.

**Spoken:**
> "Let's first run the normal streaming path.
> 
> When I click 'Send Message', the client creates a conversation and starts a single run. The connection badge switches to `Connecting`, then `Connected`.
> 
> As you can see, text events stream in progressively word by word. Notice the cursor counter at the top: it increments monotonically from 1 up to 33.
> 
> When generation completes, the server sends a terminal `done` event, the connection status switches to `Completed`, and the run is marked completed in the database."

---

## 1:30 – 2:30 | Part 3: Connection Interruption & Cursor Recovery (AC2, AC3)

**Action on screen:**
1. Click **Send Message**.
2. When words reach around sequence 6 or 7, click **Simulate Disconnect**.
3. Point to UI switching to `Reconnecting` and cursor freezing at `7`.
4. Wait 1-2 seconds as auto-reconnect kicks in, replaying events `8..33`.

**Spoken:**
> "Now let's demonstrate the core scenario: network drop and recovery.
> 
> I'll start a new stream, and at chunk 7, I'll click 'Simulate Disconnect'.
> 
> Notice what happened:
> 1. The client connection dropped and the status became `Reconnecting`.
> 2. The client cursor checkpoint froze at sequence 7.
> 3. Crucially, the server generator did NOT restart and did NOT stop; it continued generating chunks 8, 9, 10... and persisting them to SQLite.
> 4. The client's automatic reconnect fired with `?after=7`.
> 5. The server replayed the missed events directly from SQLite, transitioned into live delivery, and completed the response.
> 
> Looking at the chat box and the events table below: there are zero duplicate words, zero missing chunks, and the conversation preserved the exact same `Run ID` throughout."

---

## 2:30 – 3:15 | Part 4: Generator Failure Recovery (AC5)

**Action on screen:**
1. Check the box: **Simulate Failure after 5 chunks**.
2. Click **Send Message**.
3. Watch the assistant stream 5 words and then display `[ERROR: Simulated generator failure after emitting 5 events.]` with status `Failed`.

**Spoken:**
> "Next, let's look at failure handling. If the model or generator fails mid-stream, we must not leave the client hanging or report false completion.
> 
> I'll check 'Simulate Failure after 5 chunks' and send.
> 
> The stream emits 5 words, and then the server catches the failure, records an `error` event in SQLite, and transitions the run status to `Failed`. The client renders the error message and closes the stream. In the database, the partial history is preserved, and this run can never become completed."

---

## 3:15 – 4:00 | Part 5: Automated Verification Benchmark

**Action on screen:**
1. Switch to terminal.
2. Run: `dotnet test --filter "Category=Benchmark" --logger "console;verbosity=detailed"`
3. Show the printed benchmark report.

**Spoken:**
> "To verify this deterministically, I built an automated verification benchmark meeting all Caygnus criteria. Let's run it now.
> 
> Look at the report:
> - Expected events: 33
> - Events received before interruption: 7
> - Reconnect cursor: 7
> - Events recovered after reconnect: 26
> - Missing events: 0
> - Duplicate events: 0
> - Out-of-order events: 0
> - Final run state: `completed`
> - Result: PASS in under 4 seconds without paid model APIs or flaky long sleeps."

---

## 4:00 – 4:30 | Part 6: Important Trade-off & Conclusion

**Spoken:**
> "Finally, one important engineering trade-off: **Service Restart Semantics**.
> 
> When the service process restarts, what happens to in-progress runs? Instead of attempting to resume a half-generated stream from an arbitrary token, on startup the server transitions any lingering in-progress runs to `interrupted` and appends an interruption event.
> 
> This provides honest, deterministic semantics: persisted history is never lost, but we don't pretend generation continued when the process died. In production with multiple nodes, we would replace the local in-memory broadcaster with a distributed stream such as Redis Streams or Kafka.
> 
> Thank you for reviewing my submission!"
