-- Run inside a transaction.
-- If the selected row is an expired lease, close its previous open attempt
-- as lease_expired before inserting the new attempt.

WITH candidate AS (
    SELECT id
    FROM crawl_urls
    WHERE crawl_run_id = @run_id
      AND (
          (state IN ('pending', 'retry_scheduled') AND available_at <= now())
          OR
          (state = 'leased' AND lease_until <= now())
      )
    ORDER BY
        depth ASC,
        priority DESC,
        available_at ASC,
        discovered_at ASC,
        id ASC
    FOR UPDATE SKIP LOCKED
    LIMIT 1
)
UPDATE crawl_urls AS u
SET state = 'leased',
    lease_token = @lease_token,
    lease_owner = @worker_id,
    lease_until = now() + @lease_duration,
    attempt_count = attempt_count + 1,
    updated_at = now()
FROM candidate
WHERE u.id = candidate.id
RETURNING
    u.id,
    u.crawl_run_id,
    u.canonical_url,
    u.depth,
    u.priority,
    u.attempt_count,
    u.lease_token,
    u.lease_until;
