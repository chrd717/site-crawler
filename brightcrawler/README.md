# BrightCrawler

A resumable concurrent site crawler written in C#/.NET 10. It consumes the provided Fetch API, follows in-scope references, processes HTML/images/videos/PDFs, stores raw artifacts by type, and persists scheduling state and metadata in PostgreSQL.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json` as `10.0.301`)
- Docker (for PostgreSQL and integration tests)

## Run

**Local development** — database in Docker, crawler on the host:

```bash
docker compose up -d postgres
dotnet run --project src/BrightCrawler.App -- init-db
dotnet run --project src/BrightCrawler.App -- crawl https://example.com
```

Resume or inspect a run:

```bash
dotnet run --project src/BrightCrawler.App -- resume <run-id>
dotnet run --project src/BrightCrawler.App -- status <run-id>
```

Press `Ctrl+C` during a crawl to pause gracefully; the run is persisted as `paused` and can be resumed later.

**Full stack** — database and crawler in containers:

```bash
docker compose --profile full up --build
```

The `postgres` service starts by default; the `crawler` service is behind the `full` profile so day-to-day dev does not require rebuilding the app image.

## Architecture

The solution is a pragmatic modular monolith with three projects: `BrightCrawler.App`, `BrightCrawler.Core`, and `BrightCrawler.Infrastructure`.

PostgreSQL is the **durable crawl frontier**: it registers canonical URLs, prevents duplicate scheduling, leases eligible work to concurrent workers, schedules retries, recovers expired leases, and retains terminal URL history as an inspectable crawl ledger.

`crawl_urls` physically stores both active frontier entries (`pending`, `leased`, `retry_scheduled`) and terminal records. These are logically distinct: the active frontier grows and shrinks, while terminal records remain for deduplication, resumability, and inspection.

Concurrent workers lease rows with `FOR UPDATE SKIP LOCKED`. Every lease has a unique token; completion and retry updates compare that token so a stale worker cannot commit after its lease is reclaimed.

See [`ARCHITECTURE.md`](ARCHITECTURE.md) for the full design baseline and [`INTERVIEW_DEFENSE.md`](INTERVIEW_DEFENSE.md) for interview talking points.

## Correctness guarantees

The crawler guarantees one canonical URL identity per crawl run, one valid active lease, fenced finalization, and idempotent replay. It does **not** claim strict exactly-once HTTP fetching because an external request and PostgreSQL commit cannot be atomic. A fetch may repeat after a transient failure or process crash without creating a duplicate logical URL.

## Resilience and rate control

Expected HTTP responses are explicit outcomes. `403` and `404` are terminal. `500` and network failures use bounded exponential backoff with full jitter. `429` honors `Retry-After`, persists the URL's next eligibility time, and pauses the shared outbound request gate. No hidden `HttpClient`/Polly retries are used.

## Content processing

Dispatch is based on normalized response `Content-Type`, not the URL extension. Four `IContentProcessor` implementations extract the required metadata. Adding another content type requires a processor, one DI registration, and tests; the crawl pipeline does not change.

Artifacts are written atomically below `output/html`, `output/images`, `output/videos`, and `output/pdfs` using content hashes. PostgreSQL maps every URL to its artifact, metadata, attempts, and discovery relationships.

## Deliberate trade-offs

PostgreSQL replaces a separate broker at this scale and avoids queue/database dual-write consistency. Redis, MediatR, AutoMapper, generic repositories, and an external logging stack are intentionally absent because they do not solve an independent requirement.

The take-home defaults to exact-host scope and does not execute JavaScript or implement complete robots/public-suffix/private-network policy. Video duration is best effort. These limits keep the solution small while preserving a clear production path.

## What I would improve next

- Partition and index the frontier table at very high URL volume
- Persist global rate-limit state per API credential across process restarts
- OpenTelemetry metrics, structured run dashboards, and orphan artifact GC
- `LISTEN/NOTIFY` wakeups when only future retries remain instead of polling
- Object storage for artifacts and multi-instance worker fleets on the same lease protocol

## Tests

```bash
dotnet test BrightCrawler.sln
```

| Layer | What it proves |
|---|---|
| Unit | Canonicalization, scope, `Retry-After` parsing |
| Integration (frontier) | PostgreSQL leases, stale token rejection, resume metadata |
| E2E + `InMemoryFetchApiClient` | Full crawl pipeline, DB state, artifacts, paused resume |

E2E scenarios: cyclic graph `A→B→C→A`, duplicate link dedup, `429→200` retry, MIME-based PDF dispatch, paused-run resume.

## Production evolution

At moderate scale, run multiple worker instances on the same lease protocol, move artifacts to object storage, add OpenTelemetry, and partition/index frontier data. Introduce a broker only when fetch and processing must scale independently, through an outbox/inbox protocol. The frontier remains the scheduling source of truth.
