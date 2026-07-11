# BrightCrawler Architecture Package v2

This package is the updated architecture baseline for the C# crawler take-home.

## Recommended reading order

1. [`ARCHITECTURE.md`](ARCHITECTURE.md) — complete submission-ready architecture.
2. [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) — risk-first one-day delivery order.
3. [`PROJECT_TEMPLATE.md`](PROJECT_TEMPLATE.md) — solution/projects/dependency template.
4. [`README-outline.md`](README-outline.md) — concise repository README draft.

Design decisions for the implemented solution: [`../brightcrawler/DESIGN_DECISIONS.md`](../brightcrawler/DESIGN_DECISIONS.md).

## Supporting material

- `docs/adr/0001-postgresql-durable-crawl-frontier.md` — why PostgreSQL owns frontier state.
- `docs/adr/0002-processing-guarantees.md` — why the design uses leases and replay instead of claiming exactly-once fetch.
- `docs/sql/001_initial.sql` — aligned schema draft.
- `docs/sql/CLAIM_NEXT.sql` — concurrency-critical lease query.
- `.cursor/rules/` — architecture, crawler-domain, C#, and testing rules.
- `REFERENCES.md` — primary technical references.
- `CHANGES_FROM_V1.md` — summary of the architecture revision.

## Central decision

The key abstraction is `ICrawlFrontier`, implemented by `PostgresCrawlFrontier`. PostgreSQL is not treated as a generic repository or a simple FIFO queue: it owns canonical URL identity, eligibility, ordering, leases, retries, recovery, and terminal crawl history.
