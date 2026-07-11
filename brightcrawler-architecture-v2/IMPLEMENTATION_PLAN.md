# One-Day Implementation Plan

The order is risk-first: prove frontier correctness before adding format handlers or polish.

## P0.1 — solution and database

- Create App/Core/Infrastructure and two test projects.
- Enable nullable, analyzers, warnings as errors, deterministic builds, and central package versions.
- Add PostgreSQL to `compose.yaml`.
- Apply `001_initial.sql` at startup or through a minimal migration runner.
- Implement run creation with seed insertion.

**Exit condition:** a run and canonical seed survive process restart.

## P0.2 — durable crawl frontier

- Implement `ICrawlFrontier` and `PostgresCrawlFrontier`.
- Add canonical URL hash uniqueness.
- Implement atomic leasing with `FOR UPDATE SKIP LOCKED`.
- Add lease token fencing and expired lease recovery.
- Insert/finish attempt rows in the same transactions as state changes.
- Implement `FrontierSnapshot` and completion detection.

**Exit condition:** concurrent discovery and claim integration tests pass against real PostgreSQL.

## P0.3 — crawl workflow and policies

- Implement conservative URL canonicalization and exact-host scope.
- Implement `FetchOutcomePolicy` and `RetryPlanner` as pure code.
- Implement coordinator, worker loops, and single-URL pipeline.
- Use `TimeProvider` for delays and retry calculations.
- Add fake-fetch tests for a cyclic graph and duplicate links.

**Exit condition:** `A → B → C → A` finishes with exactly three logical frontier entries.

## P0.4 — rate control and resilience

- Add max in-flight request control.
- Add token-bucket request start rate.
- Parse both forms of `Retry-After`.
- Apply durable per-entry retry and process-wide cooldown.
- Add bounded backoff with full jitter for `500` and transport failures.
- Validate fetch/processing timeout against lease duration.

**Exit condition:** `429 → 200` test proves retry scheduling and no hidden retries.

## P0.5 — content and artifact persistence

- Add processor registry and four processors.
- Dispatch by normalized `Content-Type`, never extension.
- Add content-addressed atomic filesystem writes.
- Persist common fields and JSONB metadata.
- Commit successful HTML plus discovered URLs in one transaction.

**Exit condition:** misleading extension, metadata extraction, and atomic discovery tests pass.

## P0.6 — operational finish

- Add structured JSON logs and stable event names.
- Handle Ctrl+C/graceful cancellation.
- Add final progress summary.
- Add Dockerfile, concise README, architecture diagram, and known limitations.
- Run formatting, build, tests, and a clean Compose smoke test.

## P1 — only after P0 is green

- `status <run-id>` command;
- richer discovery graph persistence;
- optional lease heartbeat;
- additional video metadata support;
- subdomain/allowlist scope options;
- orphan artifact cleanup.

## Stop rule

Do not add optional infrastructure while any of these remains unproven:

- one canonical frontier entry under concurrent discovery;
- distinct leases under concurrent workers;
- stale lease token rejection;
- expired lease recovery;
- durable retry after restart;
- HTML success and discoveries committed atomically;
- MIME dispatch independent of URL extension.
