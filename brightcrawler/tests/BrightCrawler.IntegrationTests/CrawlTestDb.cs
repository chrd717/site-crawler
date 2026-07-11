using Npgsql;

namespace BrightCrawler.IntegrationTests;

internal static class CrawlTestDb
{
    public static async Task<int> CountUrlsAsync(
        string connectionString,
        Guid runId,
        string? state = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = state is null
            ? "SELECT COUNT(*) FROM crawl_urls WHERE crawl_run_id = @run_id"
            : "SELECT COUNT(*) FROM crawl_urls WHERE crawl_run_id = @run_id AND state = @state";

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("run_id", runId);
        if (state is not null)
        {
            cmd.Parameters.AddWithValue("state", state);
        }

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public static async Task<IReadOnlyList<string>> GetCanonicalUrlsAsync(
        string connectionString,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT canonical_url FROM crawl_urls WHERE crawl_run_id = @run_id ORDER BY canonical_url",
            connection);
        cmd.Parameters.AddWithValue("run_id", runId);

        var urls = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            urls.Add(reader.GetString(0));
        }

        return urls;
    }

    public static async Task<IReadOnlyList<CrawlAttemptRow>> GetAttemptsAsync(
        string connectionString,
        Guid runId,
        string canonicalUrl,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT a.attempt_number, a.outcome, a.http_status, a.retry_at
            FROM crawl_attempts a
            JOIN crawl_urls u ON u.id = a.crawl_url_id
            WHERE u.crawl_run_id = @run_id AND u.canonical_url = @canonical_url
            ORDER BY a.attempt_number
            """,
            connection);
        cmd.Parameters.AddWithValue("run_id", runId);
        cmd.Parameters.AddWithValue("canonical_url", canonicalUrl);

        var attempts = new List<CrawlAttemptRow>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attempts.Add(new CrawlAttemptRow
            {
                AttemptNumber = reader.GetInt32(0),
                Outcome = await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1),
                HttpStatus = await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetInt32(2),
                RetryAt = await reader.IsDBNullAsync(3, cancellationToken)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(3)
            });
        }

        return attempts;
    }

    public static async Task<string?> GetArtifactPathAsync(
        string connectionString,
        Guid runId,
        string canonicalUrl,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT artifact_path FROM crawl_urls
            WHERE crawl_run_id = @run_id AND canonical_url = @canonical_url
            """,
            connection);
        cmd.Parameters.AddWithValue("run_id", runId);
        cmd.Parameters.AddWithValue("canonical_url", canonicalUrl);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is string path ? path : null;
    }
}

internal sealed record CrawlAttemptRow
{
    public required int AttemptNumber { get; init; }
    public string? Outcome { get; init; }
    public int? HttpStatus { get; init; }
    public DateTimeOffset? RetryAt { get; init; }
}
