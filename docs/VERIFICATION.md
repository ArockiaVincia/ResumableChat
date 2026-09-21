# Verification Benchmark

## Purpose

This benchmark verifies the core acceptance criteria of **Problem 1: Resumable Realtime Conversation**:
1. Generates $\ge 30$ sequentially ordered text events for a single run.
2. Simulates an in-flight network disconnection / drop while generation is actively running on the server.
3. Allows the server's background generator to continue persisting events to SQLite.
4. Reconnects from the client's last processed cursor checkpoint (`after={lastProcessedSequence}`).
5. Reconstructs the complete response stream with **zero missing events**, **zero duplicate events**, and strictly ascending sequence order.
6. Verifies that the run reaches the `completed` state without generating duplicate runs.

---

## How to Run

Execute either command from the repository root:

```powershell
# Option 1: Using dotnet test with Category filter
dotnet test --filter "Category=Benchmark" --logger "console;verbosity=normal"

# Option 2: Using the benchmark runner script
powershell -ExecutionPolicy Bypass -File scripts/run-benchmark.ps1
```

---

## Expected vs. Observed Behavior

| Metric | Target / Specification | Observed Benchmark Result | Status |
| :--- | :--- | :--- | :--- |
| **Total Events Generated** | $\ge 30$ | **33** (32 text chunks + 1 done) | PASS |
| **Interruption Point** | While actively streaming | **7 events received** | PASS |
| **Client Reconnect Cursor** | Last processed sequence | **`after=7`** | PASS |
| **Events Recovered on Reconnect** | Remaining sequences $8..33$ | **26 events** | PASS |
| **Total Events Reconstructed** | Exactly $33$ | **33 events** | PASS |
| **Missing Events** | $0$ | **0** | PASS |
| **Duplicate Events** | $0$ | **0** | PASS |
| **Out-of-Order Events** | $0$ | **0** | PASS |
| **Preserved Conversation & Run** | Exactly 1 run in conversation | **1 run preserved** | PASS |
| **Final Run State** | `completed` | **`completed`** | PASS |

---

## Sample Benchmark Output

```text
=== Resumable Conversation Verification Benchmark ===

Run ID: d59ea8ec76524ba180e220f476a0904d
Conversation ID: 562dc211ae924faabcc998bba6230bc1

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
