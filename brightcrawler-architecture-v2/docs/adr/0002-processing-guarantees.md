# ADR 0002: Use exclusive leases and replay-safe processing instead of claiming exactly-once fetch

- Status: Accepted

## Context

The assignment asks that each URL be processed at most once under concurrency. The crawler also has to retry transient failures and resume after crashes. The external Fetch API and PostgreSQL cannot participate in one atomic transaction.

## Decision

Define guarantees at separate layers:

1. one canonical URL identity per crawl run;
2. one valid non-expired lease token per entry;
3. finalization conditional on the current lease token;
4. multiple observable attempts are permitted for retries or expired leases;
5. artifacts and database completion are idempotent/replay-safe.

Do not describe external fetching as exactly once.

## Rationale

Marking an entry complete before fetching can lose work after a crash. Marking it complete after fetching can repeat a fetch after a crash. The latter is safer when side effects are idempotent and stale workers are fenced from database completion.

## Consequences

- a transient failure or crash may repeat network and parsing work;
- no logical URL is concurrently finalized by two workers;
- attempt history remains truthful;
- the system resumes without losing undiscovered links from a successfully committed HTML page.
