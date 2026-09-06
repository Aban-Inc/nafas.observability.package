using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nafas.Observability.Storage;

namespace Nafas.Observability
{
    /// <summary>
    /// Runs INafasSchemaProvisioner.EnsureCreatedAsync once when the host
    /// starts. A hosted service, not a blocking call inside AddNafasServer
    /// itself -- schema creation is I/O (opening a real connection, running
    /// DDL) and AddNafasServer runs synchronously during Program.cs's
    /// registration phase, before there's a running host to safely block on
    /// async work from.
    /// </summary>
    internal sealed class NafasSchemaInitializationHostedService : IHostedService
    {
        private readonly INafasSchemaProvisioner _provisioner;
        private readonly ILogger<NafasSchemaInitializationHostedService> _logger;

        public NafasSchemaInitializationHostedService(INafasSchemaProvisioner provisioner, ILogger<NafasSchemaInitializationHostedService> logger)
        {
            _provisioner = provisioner;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Ensuring Nafas tables exist...");
            await _provisioner.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Nafas tables ready.");
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
