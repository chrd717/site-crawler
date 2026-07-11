# Architecture Defense — Talking Points

## One sentence

> I chose a pragmatic modular monolith where PostgreSQL acts as the durable crawl frontier: it stores URL identity, eligibility, leases, retries, and terminal history, while workers atomically claim work directly via `FOR UPDATE SKIP LOCKED`.

## What is the frontier?

The frontier is not just a FIFO queue. It is the crawler's scheduler. It knows:

- which canonical URLs have been discovered;
- which are eligible now;
- which are leased to workers;
- which are deferred until `available_at`;
- which URL to take next;
- whether the crawl has finished.

The active frontier consists of `Pending`, `Leased`, and `RetryScheduled` entries. Terminal records remain in the same table as a crawl ledger for deduplication, resume, and inspection.

## Why `ICrawlFrontier`, not a repository

A repository usually exposes CRUD and allows arbitrary updates. Here we need domain-shaped atomic operations:

```text
CreateRun
TryLeaseNext
CompleteSuccess
CompleteRedirect
ScheduleRetry
CompleteTerminal
GetSnapshot
```

This prevents callers from bypassing the lease token or performing an invalid state transition.

## What guarantees does the system provide?

Do not say "exactly-once fetch."

Say this instead:

- one canonical URL is registered once per crawl run;
- only one lease is valid at a time;
- only the holder of the current lease token may finalize an entry;
- a transient failure or crash may cause a repeated fetch;
- replay is safe thanks to idempotent completion and content-addressed storage.

An external HTTP request and a PostgreSQL transaction cannot be committed atomically. Safe replay is therefore more honest and correct than promising exactly-once fetching.

## Why PostgreSQL, not RabbitMQ

PostgreSQL already provides everything the current system needs:

```text
durable state
unique canonical URL
atomic claim
retry schedule
leases
resume
attempt history
inspectability
```

RabbitMQ would add a second source of truth — broker plus database. That would require outbox/inbox, idempotent consumers, and reconciliation. At the current scale, it does not create a new capability the workload actually needs.

A broker becomes justified when fetch and processing must scale independently, when large distributed clusters appear, or when separate pipeline stages are required.

## Why no `Channel<T>`

`Channel<T>` would create a second, non-durable in-memory frontier. A URL could be leased in the database but still waiting in the channel. After a crash, separate prefetch and recovery rules would be needed. Workers are simpler and more reliable when they claim directly from PostgreSQL.

## Why retries are not in Polly

Retry is part of crawler state, not a hidden transport detail. We need to see:

```text
attempt number
status code
error
Retry-After
next available time
final outcome
```

So retries are persisted in PostgreSQL and executed as a new frontier transition. Invisible HTTP retries would break observability and attempt accounting.

## What happens on `429`

- parse `Retry-After` as delta-seconds or HTTP-date;
- compute a not-before time no earlier than the server hint;
- move the URL to `RetryScheduled`;
- persist `available_at`;
- update a shared process-wide cooldown so other workers do not keep hammering the same Fetch API.

## Why exact-host scope

This is a simple and safe policy for the take-home. It does not require a partial and potentially incorrect Public Suffix List implementation. Subdomains or an allowlist can be added as a separate policy without changing the worker pipeline.

## Why content hash in the filename

```text
output/images/ab/<sha256>.jpg
```

This solves collisions, query-string filenames, path traversal, deduplication of identical bytes, and change detection. The URL-to-artifact mapping lives in the database.

## What will look especially strong in the code

- `ICrawlFrontier` with use-case-shaped methods;
- lease token as a fencing token;
- explicit SQL claim and a PostgreSQL integration test;
- atomic HTML completion plus discovery inserts;
- `Retry-After` handling without hidden retries;
- MIME-based dispatch when the URL extension is misleading;
- a test for `A → B → C → A`;
- a deliberate omissions table in the README.

## Production evolution

First:

```text
one process + PostgreSQL + filesystem
```

Then:

```text
multiple workers + object storage + OpenTelemetry
```

At very large scale:

```text
partitioned frontier
scheduler/outbox/broker
separate fetch and processing pools
distributed rate state
compliance policy
```

Important: a broker may become the transport layer, but the frontier remains the source of scheduling state.
