using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nafas.Observability
{
    /// <summary>
    /// Deletes rows from nafas_logs/nafas_metrics/nafas_traces older than
    /// <see cref="NafasServerOptions.RetentionDays"/> (default 30). Runs once
    /// on startup, then every 6 hours -- retention doesn't need to be exact to
    /// the minute, and running it constantly would just be wasted I/O for a
    /// value that only matters at day granularity.
    /// </summary>
    internal sealed class NafasRetentionHostedService : BackgroundService
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

        private readonly NafasServerOptions _options;
        private readonly string _connectionString;
        private readonly ILogger<NafasRetentionHostedService> _logger;

        // NafasConnectionString, not a bare string -- see its own comment in
        // NafasServiceCollectionExtensions.cs for why a raw `string` is never
        // registered directly in the consuming app's DI container.
        public NafasRetentionHostedService(NafasServerOptions options, NafasConnectionString connectionString, ILogger<NafasRetentionHostedService> logger)
        {
            _options = options;
            _connectionString = connectionString.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // A failed sweep is never fatal to the host app -- log and
                    // try again next interval, same "never take down the
                    // consumer's app" posture as everything else this package
                    // does that isn't the actual dashboard request in flight.
                    _logger.LogError(ex, "Nafas retention sweep failed; will retry at the next interval.");
                }

                try
                {
                    await Task.Delay(SweepInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task SweepAsync(CancellationToken cancellationToken)
        {
            var cutoffUtc = DateTime.UtcNow.AddDays(-Math.Max(1, _options.RetentionDays));

            int deleted;
            if (_options.Provider == DatabaseProvider.Sqlite)
            {
                deleted = await SweepSqliteAsync(cutoffUtc, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                deleted = await SweepSqlServerAsync(cutoffUtc, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Nafas retention sweep removed {DeletedRows} row(s) older than {CutoffUtc:u} ({RetentionDays} day retention).", deleted, cutoffUtc, _options.RetentionDays);
        }

        private async Task<int> SweepSqliteAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var cutoff = cutoffUtc.ToString("O");
            var deleted = 0;
            foreach (var table in new[] { "nafas_logs", "nafas_metrics", "nafas_traces" })
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"DELETE FROM {table} WHERE Timestamp < @cutoff;";
                command.Parameters.AddWithValue("@cutoff", cutoff);
                deleted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return deleted;
        }

        private async Task<int> SweepSqlServerAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
        {
            var schema = string.IsNullOrWhiteSpace(_options.Schema) ? "dbo" : _options.Schema!;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var deleted = 0;
            foreach (var table in new[] { "nafas_logs", "nafas_metrics", "nafas_traces" })
            {
                using var command = connection.CreateCommand();
                // Schema was already validated against a strict identifier
                // pattern by SqlServerSchemaProvisioner at startup (same
                // NafasServerOptions.Schema value) -- safe to interpolate here
                // for the same reason it was safe there.
                command.CommandText = $"DELETE FROM [{schema}].[{table}] WHERE [Timestamp] < @cutoff;";
                command.Parameters.AddWithValue("@cutoff", cutoffUtc);
                deleted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            return deleted;
        }
    }
}
