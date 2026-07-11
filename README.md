# Site Crawler — Take-Home Assignment

Senior take-home: a production-grade site crawler for the Fetch API described in [`senior_assignment.md`](senior_assignment.md).

## Repository layout

| Path | Purpose |
|---|---|
| [`brightcrawler/`](brightcrawler/) | **Submission** — runnable .NET 10 implementation |
| [`brightcrawler-architecture-v2/`](brightcrawler-architecture-v2/) | Architecture notes and interview prep (design baseline) |
| [`senior_assignment.md`](senior_assignment.md) | Original assignment brief |

## Quick start

All commands run from `brightcrawler/`:

```bash
cd brightcrawler
docker compose up -d postgres
dotnet run --project src/BrightCrawler.App -- init-db
dotnet run --project src/BrightCrawler.App -- crawl https://example.com
```

For setup details, architecture, trade-offs, and tests, see [`brightcrawler/README.md`](brightcrawler/README.md).

## What to review first

1. [`brightcrawler/README.md`](brightcrawler/README.md) — how to run, design summary, limitations
2. [`brightcrawler/ARCHITECTURE.md`](brightcrawler/ARCHITECTURE.md) — frontier, leases, resumability, content processing
3. [`brightcrawler/INTERVIEW_DEFENSE.md`](brightcrawler/INTERVIEW_DEFENSE.md) — talking points for design review
