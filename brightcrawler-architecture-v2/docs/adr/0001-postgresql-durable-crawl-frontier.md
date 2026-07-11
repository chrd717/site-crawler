# ADR 0001: Use PostgreSQL as the durable crawl frontier

- Status: Accepted
- Context: Senior take-home crawler

## Context

The crawler needs durable URL discovery, duplicate prevention, concurrent claiming, retries, leases, resumability, attempt history, and inspection. The submission should remain small and should not add infrastructure without an independent requirement.

## Decision

Use PostgreSQL as the source of truth for the crawl frontier. Workers directly lease eligible `crawl_urls` rows with `FOR UPDATE SKIP LOCKED`. A unique canonical URL hash prevents duplicate scheduling per crawl run. Retry eligibility and leases are persisted in the same model.

Use the domain name `ICrawlFrontier` and implementation name `PostgresCrawlFrontier`. Do not expose a generic repository API.

## Consequences

### Positive

- one durable state owner;
- transactional discovery and source-page completion;
- correct multi-worker claim semantics;
- restart-safe retries and expired-lease recovery;
- easy operational inspection with SQL;
- no broker/database dual-write.

### Negative

- workers poll when no URL is currently eligible;
- very high scheduling throughput may eventually require partitioning or a dedicated scheduler;
- database load includes both state and attempt history.

## Rejected alternatives

### In-memory queue or `Channel<T>`

Rejected because it is not durable and creates a second frontier that must be reconciled after a crash.

### RabbitMQ/Kafka/SQS now

Rejected because a broker would create another state owner and require outbox/inbox consistency before the workload needs independent pipeline scaling.

### SQLite

Viable for a single-process demo, but PostgreSQL better demonstrates concurrent leases and provides a direct path to multiple worker instances.
