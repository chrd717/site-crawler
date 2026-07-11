# Changes from v1

## Domain model

- Replaced `ICrawlStore` / `PostgresCrawlStore` with `ICrawlFrontier` / `PostgresCrawlFrontier`.
- Made frontier operations use-case-shaped instead of CRUD-shaped.
- Explicitly separated the active frontier from the terminal crawl ledger while keeping one physical URL table.
- Standardized states to `Pending`, `Leased`, `RetryScheduled`, and explicit terminal outcomes.
- Replaced retry-specific naming with the general scheduling field `available_at`.

## Correctness

- Defined identity, ownership, finalization, attempt, and replay guarantees separately.
- Made `lease_token` an explicit fencing token.
- Required every finalization to reject stale leases.
- Added abandoned-attempt closure when an expired lease is reclaimed.
- Removed any implication of strict exactly-once external fetch.

## Concurrency and scheduling

- Kept direct PostgreSQL worker claims and removed any role for `Channel<T>`.
- Added depth/priority/availability ordering to the claim policy.
- Defined frontier waiting/completion semantics for active leases and future retries.

## Delivery package

- Added a concrete project template.
- Split Cursor rules into architecture, frontier, C# quality, and testing files.
- Added two ADRs and aligned SQL drafts.
- Added Russian interview-defense notes.
