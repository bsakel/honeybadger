using GitHub.Copilot.SDK;
using Honeybadger.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Honeybadger.Host.Services;

/// <summary>
/// Manages the Copilot CLI process on the host in server mode.
/// Uses the GitHub.Copilot.SDK to start and keep the CLI alive.
/// Agents connect to it via localhost:PORT.
/// </summary>
public class CopilotCliService : IHostedService, IAsyncDisposable
{
    private readonly AgentOptions _options;
    private readonly ILogger<CopilotCliService> _logger;
    private CopilotClient? _client;

    public CopilotCliService(IOptions<AgentOptions> options, ILogger<CopilotCliService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.CopilotCli.AutoStart)
        {
            _logger.LogInformation("Copilot CLI auto-start disabled (set Agent:CopilotCli:AutoStart=true to enable)");
            return;
        }

        _logger.LogInformation("Starting Copilot CLI on port {Port}", _options.CopilotCli.Port);

        _client = new CopilotClient(new CopilotClientOptions
        {
            Port = _options.CopilotCli.Port,
            AutoStart = true,
            AutoRestart = true,
            UseLoggedInUser = true,
            Logger = _logger
        });

        await _client.StartAsync(cancellationToken);
        _logger.LogInformation("Copilot CLI ready on port {Port}", _options.CopilotCli.Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Copilot CLI");
        if (_client is not null)
            await _client.StopAsync();
    }

    /// <summary>Returns the Copilot CLI endpoint: localhost:PORT</summary>
    public string GetLocalEndpoint() => $"localhost:{_options.CopilotCli.Port}";

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }
}
