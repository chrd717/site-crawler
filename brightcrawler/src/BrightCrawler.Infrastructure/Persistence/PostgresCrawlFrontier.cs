using System.Text.Json;
using BrightCrawler.Core.Frontier;
using BrightCrawler.Core.Policies;
using BrightCrawler.Core.Runs;
using Npgsql;
using NpgsqlTypes;

namespace BrightCrawler.Infrastructure.Persistence;

public sealed class PostgresCrawlFrontier : ICrawlFrontier
{
    private readonly string _connectionString;

    public PostgresCrawlFrontier(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<CrawlRunId> CreateRunAsync(
        CrawlRunDefinition definition,
        CancellationToken cancellationToken)
    {
        if (!UrlCanonicalizer.TryCanonicalize(
                definition.SeedUrl,
                out var canonical,
                out var hash))
        {
            throw new ArgumentException("Seed URL is not a valid HTTP(S) URL.", nameof(definition));
        }

        var runId = CrawlRunId.New();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var runCmd = new NpgsqlCommand(
            """
            INSERT INTO crawl_runs (id, seed_url, effective_host, state, options_json, started_at)
            VALUES (@id, @seed, @host, 'running', @options::jsonb, now())
            """, connection, transaction))
        {
            runCmd.Parameters.AddWithValue("id", runId.Value);
            runCmd.Parameters.AddWithValue("seed", definition.SeedUrl);
            runCmd.Parameters.AddWithValue("host", definition.EffectiveHost);
            runCmd.Parameters.AddWithValue("options", JsonSerializer.Serialize(definition.Options));
            await runCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var seedCmd = new NpgsqlCommand(
            """
            INSERT INTO crawl_urls (
                crawl_run_id, canonical_url, canonical_url_hash, first_seen_url,
                depth, state, available_at)
            VALUES (@run_id, @canonical, @hash, @first_seen, 0, 'pending', now())
            """, connection, transaction))
        {
            seedCmd.Parameters.AddWithValue("run_id", runId.Value);
            seedCmd.Parameters.AddWithValue("canonical", canonical);
            seedCmd.Parameters.Add("hash", NpgsqlDbType.Bytea).Value = hash;
            seedCmd.Parameters.AddWithValue("first_seen", definition.SeedUrl);
            await seedCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return runId;
    }

    public async Task<FrontierLease?> TryLeaseNextAsync(
        CrawlRunId runId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var leaseToken = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long? urlId = null;
        string canonicalUrl = string.Empty;
        int depth;
        int attemptNumber;
        DateTimeOffset leaseUntil;

        await using (var claimCmd = new NpgsqlCommand(
            """
            WITH candidate AS (
                SELECT id, state, lease_token, attempt_count
                FROM crawl_urls
                WHERE crawl_run_id = @run_id
                  AND (
                      (state IN ('pending', 'retry_scheduled') AND available_at <= now())
                      OR
                      (state = 'leased' AND lease_until <= now())
                  )
                ORDER BY depth ASC, priority DESC, available_at ASC, discovered_at ASC, id ASC
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            ),
            reclaimed AS (
                UPDATE crawl_urls AS u
                SET state = 'leased',
                    lease_token = @lease_token,
                    lease_owner = @worker_id,
                    lease_until = now() + @lease_duration,
                    attempt_count = u.attempt_count + 1,
                    updated_at = now()
                FROM candidate
                WHERE u.id = candidate.id
                RETURNING u.id, u.canonical_url, u.depth, u.attempt_count, u.lease_until, candidate.state AS prev_state, candidate.lease_token AS prev_token
            )
            SELECT id, canonical_url, depth, attempt_count, lease_until, prev_state, prev_token FROM reclaimed
            """, connection, transaction))
        {
            claimCmd.Parameters.AddWithValue("run_id", runId.Value);
            claimCmd.Parameters.AddWithValue("lease_token", leaseToken);
            claimCmd.Parameters.AddWithValue("worker_id", workerId);
            claimCmd.Parameters.AddWithValue("lease_duration", leaseDuration);

            await using var reader = await claimCmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.CloseAsync();
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            urlId = reader.GetInt64(0);
            canonicalUrl = reader.GetString(1);
            depth = reader.GetInt32(2);
            attemptNumber = reader.GetInt32(3);
            leaseUntil = reader.GetFieldValue<DateTimeOffset>(4);
            var prevState = reader.GetString(5);
            var prevToken = await reader.IsDBNullAsync(6, cancellationToken)
                ? (Guid?)null
                : reader.GetGuid(6);

            if (prevState == "leased" && prevToken.HasValue)
            {
                await reader.CloseAsync();
                await CloseAttemptAsync(
                    connection,
                    transaction,
                    urlId.Value,
                    prevToken.Value,
                    "lease_expired",
                    cancellationToken);
            }
            else
            {
                await reader.CloseAsync();
            }
        }

        await using (var attemptCmd = new NpgsqlCommand(
            """
            INSERT INTO crawl_attempts (crawl_url_id, attempt_number, lease_token, worker_id, started_at)
            VALUES (@url_id, @attempt, @lease_token, @worker_id, now())
            """, connection, transaction))
        {
            attemptCmd.Parameters.AddWithValue("url_id", urlId!.Value);
            attemptCmd.Parameters.AddWithValue("attempt", attemptNumber);
            attemptCmd.Parameters.AddWithValue("lease_token", leaseToken);
            attemptCmd.Parameters.AddWithValue("worker_id", workerId);
            await attemptCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new FrontierLease
        {
            RunId = runId,
            EntryId = new FrontierEntryId(urlId!.Value),
            LeaseToken = leaseToken,
            WorkerId = workerId,
            AttemptNumber = attemptNumber,
            CanonicalUrl = canonicalUrl,
            Depth = depth,
            LeaseUntil = leaseUntil
        };
    }

    public async Task<bool> CompleteSuccessAsync(
        FrontierLease lease,
        CrawlCompletion completion,
        IReadOnlyCollection<UrlDiscovery> discoveries,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var updated = await FinalizeEntryAsync(
            connection,
            transaction,
            lease,
            "succeeded",
            completion.HttpStatus,
            null,
            null,
            completion.MediaType,
            completion.DeclaredLength,
            completion.ActualLength,
            completion.ContentSha256,
            completion.ArtifactPath,
            completion.MetadataJson,
            completion.ETag,
            completion.LastModified,
            cancellationToken);

        if (!updated)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await FinishAttemptAsync(
            connection,
            transaction,
            lease,
            "succeeded",
            completion.HttpStatus,
            completion.MediaType,
            completion.DeclaredLength,
            completion.ActualLength,
            completion.ContentSha256,
            completion.ETag,
            completion.LastModified,
            null,
            null,
            null,
            cancellationToken);

        foreach (var discovery in discoveries)
        {
            await InsertDiscoveryAsync(connection, transaction, lease, discovery, cancellationToken);
        }

        await using (var runCmd = new NpgsqlCommand(
            """
            UPDATE crawl_runs
            SET downloaded_bytes = downloaded_bytes + @bytes,
                known_url_count = (
                    SELECT COUNT(*) FROM crawl_urls WHERE crawl_run_id = @run_id)
            WHERE id = @run_id
            """, connection, transaction))
        {
            runCmd.Parameters.AddWithValue("bytes", completion.ActualLength);
            runCmd.Parameters.AddWithValue("run_id", lease.RunId.Value);
            await runCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CompleteRedirectAsync(
        FrontierLease lease,
        RedirectCompletion redirect,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long? targetId = await UpsertPendingUrlAsync(
            connection,
            transaction,
            lease.RunId,
            redirect.TargetCanonicalUrl,
            redirect.TargetCanonicalUrlHash,
            redirect.Location,
            redirect.TargetDepth,
            lease.EntryId.Value,
            cancellationToken);

        var updated = await FinalizeEntryAsync(
            connection,
            transaction,
            lease,
            "redirected",
            redirect.HttpStatus,
            "redirect",
            $"Redirected to {redirect.TargetCanonicalUrl}",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            cancellationToken,
            redirectTargetUrlId: targetId);

        if (!updated)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        if (targetId.HasValue)
        {
            await using var linkCmd = new NpgsqlCommand(
                """
                INSERT INTO crawl_links (source_url_id, target_url_id, relation_kind, raw_reference)
                VALUES (@source, @target, 'redirect', @raw)
                ON CONFLICT DO NOTHING
                """, connection, transaction);
            linkCmd.Parameters.AddWithValue("source", lease.EntryId.Value);
            linkCmd.Parameters.AddWithValue("target", targetId.Value);
            linkCmd.Parameters.AddWithValue("raw", redirect.Location);
            await linkCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await FinishAttemptAsync(
            connection,
            transaction,
            lease,
            "redirected",
            redirect.HttpStatus,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            redirect.Location,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ScheduleRetryAsync(
        FrontierLease lease,
        RetryPlan retry,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE crawl_urls
            SET state = 'retry_scheduled',
                available_at = @available_at,
                lease_token = NULL,
                lease_owner = NULL,
                lease_until = NULL,
                last_http_status = @status,
                last_error_code = @error_code,
                last_error_message = @error_message,
                updated_at = now()
            WHERE id = @id AND state = 'leased' AND lease_token = @lease_token
            """, connection, transaction);

        cmd.Parameters.AddWithValue("available_at", retry.AvailableAt);
        cmd.Parameters.AddWithValue("status", (object?)retry.HttpStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_code", retry.ErrorCode);
        cmd.Parameters.AddWithValue("error_message", (object?)Truncate(retry.ErrorMessage) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", lease.EntryId.Value);
        cmd.Parameters.AddWithValue("lease_token", lease.LeaseToken);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (rows == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await FinishAttemptAsync(
            connection,
            transaction,
            lease,
            "retry_scheduled",
            retry.HttpStatus,
            null,
            null,
            null,
            null,
            null,
            null,
            retry.ErrorCode,
            retry.ErrorMessage,
            null,
            cancellationToken,
            retry.AvailableAt,
            retry.RetryAfterRaw);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CompleteTerminalAsync(
        FrontierLease lease,
        TerminalOutcome outcome,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var updated = await FinalizeEntryAsync(
            connection,
            transaction,
            lease,
            MapState(outcome.State),
            outcome.HttpStatus,
            outcome.ErrorCode,
            outcome.ErrorMessage,
            outcome.MediaType,
            null,
            outcome.ActualLength,
            null,
            null,
            null,
            null,
            null,
            cancellationToken);

        if (!updated)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await FinishAttemptAsync(
            connection,
            transaction,
            lease,
            MapState(outcome.State),
            outcome.HttpStatus,
            outcome.MediaType,
            null,
            outcome.ActualLength,
            null,
            null,
            null,
            outcome.ErrorCode,
            outcome.ErrorMessage,
            null,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<FrontierSnapshot> GetSnapshotAsync(
        CrawlRunId runId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                COUNT(*) FILTER (WHERE state = 'pending') AS pending,
                COUNT(*) FILTER (WHERE state = 'leased') AS leased,
                COUNT(*) FILTER (WHERE state = 'retry_scheduled') AS retry_scheduled,
                COUNT(*) FILTER (WHERE state NOT IN ('pending', 'leased', 'retry_scheduled')) AS terminal,
                MIN(available_at) FILTER (WHERE state = 'retry_scheduled' AND available_at > now()) AS next_available,
                MIN(discovered_at) FILTER (WHERE state = 'pending') AS oldest_pending
            FROM crawl_urls
            WHERE crawl_run_id = @run_id
            """, connection);

        cmd.Parameters.AddWithValue("run_id", runId.Value);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var oldestPending = await reader.IsDBNullAsync(5, cancellationToken)
            ? (DateTimeOffset?)null
            : reader.GetFieldValue<DateTimeOffset>(5);

        return new FrontierSnapshot
        {
            PendingCount = reader.GetInt32(0),
            LeasedCount = reader.GetInt32(1),
            RetryScheduledCount = reader.GetInt32(2),
            TerminalCount = reader.GetInt32(3),
            NextAvailableAt = await reader.IsDBNullAsync(4, cancellationToken)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(4),
            OldestPendingAge = oldestPending is null
                ? null
                : DateTimeOffset.UtcNow - oldestPending
        };
    }

    public async Task MarkRunCompletedAsync(
        CrawlRunId runId,
        CrawlRunState state,
        string? stopReason,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE crawl_runs
            SET state = @state, completed_at = now(), stop_reason = @reason
            WHERE id = @id
            """, connection);

        cmd.Parameters.AddWithValue("state", MapRunState(state));
        cmd.Parameters.AddWithValue("reason", (object?)stopReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", runId.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> FinalizeEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FrontierLease lease,
        string state,
        int? httpStatus,
        string? errorCode,
        string? errorMessage,
        string? mediaType,
        long? declaredLength,
        long? actualLength,
        byte[]? contentSha256,
        string? artifactPath,
        string? metadataJson,
        string? etag,
        string? lastModified,
        CancellationToken cancellationToken,
        long? redirectTargetUrlId = null)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE crawl_urls
            SET state = @state,
                lease_token = NULL,
                lease_owner = NULL,
                lease_until = NULL,
                last_http_status = @http_status,
                last_error_code = @error_code,
                last_error_message = @error_message,
                media_type = @media_type,
                declared_length = @declared_length,
                actual_length = @actual_length,
                content_sha256 = @content_sha256,
                artifact_path = @artifact_path,
                metadata_json = @metadata_json::jsonb,
                etag = @etag,
                last_modified = @last_modified,
                redirect_target_url_id = @redirect_target,
                completed_at = now(),
                updated_at = now()
            WHERE id = @id AND state = 'leased' AND lease_token = @lease_token
            """, connection, transaction);

        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("http_status", (object?)httpStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_code", (object?)errorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_message", (object?)Truncate(errorMessage) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("media_type", (object?)mediaType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("declared_length", (object?)declaredLength ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actual_length", (object?)actualLength ?? DBNull.Value);
        cmd.Parameters.Add("content_sha256", NpgsqlDbType.Bytea).Value =
            (object?)contentSha256 ?? DBNull.Value;
        cmd.Parameters.AddWithValue("artifact_path", (object?)artifactPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("metadata_json", (object?)metadataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("etag", (object?)etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("last_modified", (object?)lastModified ?? DBNull.Value);
        cmd.Parameters.AddWithValue("redirect_target", (object?)redirectTargetUrlId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", lease.EntryId.Value);
        cmd.Parameters.AddWithValue("lease_token", lease.LeaseToken);

        return await cmd.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task FinishAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FrontierLease lease,
        string outcome,
        int? httpStatus,
        string? mediaType,
        long? declaredLength,
        long? actualLength,
        byte[]? contentSha256,
        string? etag,
        string? lastModified,
        string? errorCode,
        string? errorMessage,
        string? location,
        CancellationToken cancellationToken,
        DateTimeOffset? retryAt = null,
        string? retryAfterRaw = null)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE crawl_attempts
            SET finished_at = now(),
                outcome = @outcome,
                http_status = @http_status,
                media_type = @media_type,
                declared_length = @declared_length,
                actual_length = @actual_length,
                content_sha256 = @content_sha256,
                etag = @etag,
                last_modified = @last_modified,
                error_code = @error_code,
                error_message = @error_message,
                location = @location,
                retry_at = @retry_at,
                retry_after_raw = @retry_after_raw
            WHERE crawl_url_id = @url_id AND lease_token = @lease_token
            """, connection, transaction);

        cmd.Parameters.AddWithValue("outcome", outcome);
        cmd.Parameters.AddWithValue("http_status", (object?)httpStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("media_type", (object?)mediaType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("declared_length", (object?)declaredLength ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actual_length", (object?)actualLength ?? DBNull.Value);
        cmd.Parameters.Add("content_sha256", NpgsqlDbType.Bytea).Value =
            (object?)contentSha256 ?? DBNull.Value;
        cmd.Parameters.AddWithValue("etag", (object?)etag ?? DBNull.Value);
        cmd.Parameters.AddWithValue("last_modified", (object?)lastModified ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_code", (object?)errorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("error_message", (object?)Truncate(errorMessage) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("location", (object?)location ?? DBNull.Value);
        cmd.Parameters.AddWithValue("retry_at", (object?)retryAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("retry_after_raw", (object?)retryAfterRaw ?? DBNull.Value);
        cmd.Parameters.AddWithValue("url_id", lease.EntryId.Value);
        cmd.Parameters.AddWithValue("lease_token", lease.LeaseToken);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CloseAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long urlId,
        Guid leaseToken,
        string outcome,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE crawl_attempts
            SET finished_at = now(), outcome = @outcome
            WHERE crawl_url_id = @url_id AND lease_token = @lease_token AND finished_at IS NULL
            """, connection, transaction);

        cmd.Parameters.AddWithValue("outcome", outcome);
        cmd.Parameters.AddWithValue("url_id", urlId);
        cmd.Parameters.AddWithValue("lease_token", leaseToken);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDiscoveryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FrontierLease lease,
        UrlDiscovery discovery,
        CancellationToken cancellationToken)
    {
        var targetId = await UpsertPendingUrlAsync(
            connection,
            transaction,
            lease.RunId,
            discovery.CanonicalUrl,
            discovery.CanonicalUrlHash,
            discovery.FirstSeenUrl,
            discovery.Depth,
            lease.EntryId.Value,
            cancellationToken);

        if (!targetId.HasValue)
        {
            return;
        }

        await using var linkCmd = new NpgsqlCommand(
            """
            INSERT INTO crawl_links (source_url_id, target_url_id, relation_kind, raw_reference)
            VALUES (@source, @target, @kind, @raw)
            ON CONFLICT DO NOTHING
            """, connection, transaction);

        linkCmd.Parameters.AddWithValue("source", lease.EntryId.Value);
        linkCmd.Parameters.AddWithValue("target", targetId.Value);
        linkCmd.Parameters.AddWithValue("kind", discovery.RelationKind);
        linkCmd.Parameters.AddWithValue("raw", discovery.RawReference);
        await linkCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> UpsertPendingUrlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CrawlRunId runId,
        string canonicalUrl,
        byte[] hash,
        string firstSeenUrl,
        int depth,
        long discoveredFromId,
        CancellationToken cancellationToken)
    {
        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO crawl_urls (
                crawl_run_id, canonical_url, canonical_url_hash, first_seen_url,
                first_discovered_from_url_id, depth, state, available_at)
            VALUES (@run_id, @canonical, @hash, @first_seen, @from_id, @depth, 'pending', now())
            ON CONFLICT (crawl_run_id, canonical_url_hash) DO NOTHING
            RETURNING id
            """, connection, transaction);

        insertCmd.Parameters.AddWithValue("run_id", runId.Value);
        insertCmd.Parameters.AddWithValue("canonical", canonicalUrl);
        insertCmd.Parameters.Add("hash", NpgsqlDbType.Bytea).Value = hash;
        insertCmd.Parameters.AddWithValue("first_seen", firstSeenUrl);
        insertCmd.Parameters.AddWithValue("from_id", discoveredFromId);
        insertCmd.Parameters.AddWithValue("depth", depth);

        var inserted = await insertCmd.ExecuteScalarAsync(cancellationToken);
        if (inserted is long id)
        {
            return id;
        }

        await using var selectCmd = new NpgsqlCommand(
            """
            SELECT id FROM crawl_urls
            WHERE crawl_run_id = @run_id AND canonical_url_hash = @hash
            """, connection, transaction);

        selectCmd.Parameters.AddWithValue("run_id", runId.Value);
        selectCmd.Parameters.Add("hash", NpgsqlDbType.Bytea).Value = hash;
        var existing = await selectCmd.ExecuteScalarAsync(cancellationToken);
        return existing is long existingId ? existingId : null;
    }

    private static string MapState(FrontierState state) => state switch
    {
        FrontierState.Succeeded => "succeeded",
        FrontierState.Redirected => "redirected",
        FrontierState.NotFound => "not_found",
        FrontierState.Blocked => "blocked",
        FrontierState.Unsupported => "unsupported",
        FrontierState.Rejected => "rejected",
        FrontierState.InvalidContent => "invalid_content",
        FrontierState.FailedPermanent => "failed_permanent",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static string MapRunState(CrawlRunState state) => state switch
    {
        CrawlRunState.Created => "created",
        CrawlRunState.Running => "running",
        CrawlRunState.Paused => "paused",
        CrawlRunState.Completed => "completed",
        CrawlRunState.CompletedWithFailures => "completed_with_failures",
        CrawlRunState.StoppedByBudget => "stopped_by_budget",
        CrawlRunState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static string? Truncate(string? message, int max = 500) =>
        message is null ? null : message.Length <= max ? message : message[..max];
}
