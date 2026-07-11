-- BrightCrawler initial schema.
-- Hashes are computed by the application and stored as 32-byte SHA-256 values.

CREATE TABLE crawl_runs (
    id uuid PRIMARY KEY,
    seed_url text NOT NULL,
    effective_host text NOT NULL,
    state text NOT NULL CHECK (state IN (
        'created',
        'running',
        'paused',
        'completed',
        'completed_with_failures',
        'stopped_by_budget',
        'failed'
    )),
    options_json jsonb NOT NULL,
    known_url_count bigint NOT NULL DEFAULT 0 CHECK (known_url_count >= 0),
    downloaded_bytes bigint NOT NULL DEFAULT 0 CHECK (downloaded_bytes >= 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz,
    completed_at timestamptz,
    stop_reason text
);

CREATE TABLE crawl_urls (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    crawl_run_id uuid NOT NULL REFERENCES crawl_runs(id) ON DELETE CASCADE,

    canonical_url text NOT NULL,
    canonical_url_hash bytea NOT NULL CHECK (octet_length(canonical_url_hash) = 32),
    first_seen_url text NOT NULL,
    first_discovered_from_url_id bigint REFERENCES crawl_urls(id),
    depth integer NOT NULL CHECK (depth >= 0),
    priority smallint NOT NULL DEFAULT 0,

    state text NOT NULL CHECK (state IN (
        'pending',
        'leased',
        'retry_scheduled',
        'succeeded',
        'redirected',
        'not_found',
        'blocked',
        'unsupported',
        'rejected',
        'invalid_content',
        'failed_permanent'
    )),
    available_at timestamptz NOT NULL DEFAULT now(),
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),

    lease_token uuid,
    lease_owner text,
    lease_until timestamptz,

    last_http_status integer,
    last_error_code text,
    last_error_message text,

    media_type text,
    declared_length bigint CHECK (declared_length IS NULL OR declared_length >= 0),
    actual_length bigint CHECK (actual_length IS NULL OR actual_length >= 0),
    content_sha256 bytea CHECK (content_sha256 IS NULL OR octet_length(content_sha256) = 32),
    artifact_path text,
    metadata_json jsonb,
    etag text,
    last_modified text,
    redirect_target_url_id bigint REFERENCES crawl_urls(id),

    discovered_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz,

    CONSTRAINT uq_crawl_url_identity
        UNIQUE (crawl_run_id, canonical_url_hash),

    CONSTRAINT ck_frontier_lease_shape CHECK (
        (
            state = 'leased'
            AND lease_token IS NOT NULL
            AND lease_owner IS NOT NULL
            AND lease_until IS NOT NULL
            AND completed_at IS NULL
        )
        OR
        (
            state <> 'leased'
            AND lease_token IS NULL
            AND lease_owner IS NULL
            AND lease_until IS NULL
        )
    ),

    CONSTRAINT ck_frontier_completion_shape CHECK (
        (
            state IN ('pending', 'leased', 'retry_scheduled')
            AND completed_at IS NULL
        )
        OR
        (
            state IN (
                'succeeded', 'redirected', 'not_found', 'blocked',
                'unsupported', 'rejected', 'invalid_content', 'failed_permanent'
            )
            AND completed_at IS NOT NULL
        )
    )
);

CREATE INDEX ix_frontier_ready
    ON crawl_urls (
        crawl_run_id,
        available_at,
        depth,
        priority DESC,
        discovered_at,
        id
    )
    WHERE state IN ('pending', 'retry_scheduled');

CREATE INDEX ix_frontier_expired_leases
    ON crawl_urls (crawl_run_id, lease_until, id)
    WHERE state = 'leased';

CREATE INDEX ix_frontier_state_summary
    ON crawl_urls (crawl_run_id, state);

CREATE TABLE crawl_attempts (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    crawl_url_id bigint NOT NULL REFERENCES crawl_urls(id) ON DELETE CASCADE,
    attempt_number integer NOT NULL CHECK (attempt_number > 0),
    lease_token uuid NOT NULL,
    worker_id text NOT NULL,
    started_at timestamptz NOT NULL,
    finished_at timestamptz,
    outcome text,
    http_status integer,
    media_type text,
    declared_length bigint CHECK (declared_length IS NULL OR declared_length >= 0),
    actual_length bigint CHECK (actual_length IS NULL OR actual_length >= 0),
    retry_at timestamptz,
    retry_after_raw text,
    location text,
    etag text,
    last_modified text,
    content_sha256 bytea CHECK (content_sha256 IS NULL OR octet_length(content_sha256) = 32),
    error_code text,
    error_message text,

    CONSTRAINT uq_crawl_attempt
        UNIQUE (crawl_url_id, attempt_number),

    CONSTRAINT uq_crawl_attempt_lease
        UNIQUE (crawl_url_id, lease_token)
);

CREATE INDEX ix_crawl_attempts_url
    ON crawl_attempts (crawl_url_id, attempt_number DESC);

CREATE TABLE crawl_links (
    source_url_id bigint NOT NULL REFERENCES crawl_urls(id) ON DELETE CASCADE,
    target_url_id bigint NOT NULL REFERENCES crawl_urls(id) ON DELETE CASCADE,
    relation_kind text NOT NULL,
    raw_reference text NOT NULL,
    discovered_at timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (source_url_id, target_url_id, relation_kind)
);
