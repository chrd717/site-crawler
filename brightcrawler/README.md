# BrightCrawler

Production-grade site crawler built as a pragmatic clean modular monolith on .NET 10.

## Architecture

- **PostgreSQL** is the durable crawl frontier (`ICrawlFrontier` / `PostgresCrawlFrontier`).
- Workers lease URLs with `FOR UPDATE SKIP LOCKED` and finalize with lease-token fencing.
- No message broker, in-memory queue, or generic repository layer.
- Content is dispatched by MIME type through an `IContentProcessor` registry.

See `../brightcrawler-architecture-v2/ARCHITECTURE.md` for the full design baseline.
For interview talking points, see [`INTERVIEW_DEFENSE.md`](INTERVIEW_DEFENSE.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json` as `10.0.301`)
- Docker (for PostgreSQL and integration tests)

## Quick start

**Local development** — database in Docker, crawler on the host:

```bash
docker compose up -d postgres
dotnet run --project src/BrightCrawler.App -- init-db
dotnet run --project src/BrightCrawler.App -- crawl https://example.com
dotnet run --project src/BrightCrawler.App -- status <run-id>
```

**Full stack** — database and crawler in containers:

```bash
docker compose --profile full up --build
```

The `postgres` service starts by default; the `crawler` service is behind the `full` profile so day-to-day dev does not require rebuilding the app image.

## Key decisions

| Decision | Why |
|---|---|
| PostgreSQL frontier | One durable owner for identity, leases, retries, and history |
| Lease fencing | Prevents stale workers from committing after crash/reclaim |
| Replay-safe fetch | External HTTP and DB commit cannot be atomic; retries are explicit |
| MIME-based processors | Adding a fifth content type is one class + DI registration |
| Content-addressed artifacts | Collision-safe storage under `output/html|images|videos|pdfs` |

## Tests

```bash
dotnet test BrightCrawler.sln
```

Integration tests use Testcontainers for PostgreSQL.

## Production evolution

- Partition frontier table at very high URL volume
- Persist global rate-limit state per API credential
- OpenTelemetry metrics and orphan artifact GC
- Optional `LISTEN/NOTIFY` instead of polling when only future retries remain
