using System.Diagnostics;
using Honeybadger.Agent;
using Honeybadger.Agent.Tools.Core;
using Honeybadger.Core.Configuration;
using Honeybadger.Core.Interfaces;
using Honeybadger.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace Honeybadger.Host.Agents;

/// <summary>
/// Runs the agent orchestrator directly in the host process.
/// </summary>
public class LocalAgentRunner : IAgentRunner
{
    private readonly string _repoRoot;
    private readonly HoneybadgerOptions _options;
    private readonly ILogger<LocalAgentRunner> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public LocalAgentRunner(
        IOptions<HoneybadgerOptions> options,
        ILogger<LocalAgentRunner> logger,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _repoRoot = AppContext.BaseDirectory;
    }

    public async Task<AgentResponse> RunAgentAsync(
        AgentRequest request,
        Func<string, Task>? onStreamChunk = null,
        CancellationToken cancellationToken = default,
        IEnumerable<AIFunction>? tools = null)
    {
        using var _ = LogContext.PushProperty("CorrelationId", request.CorrelationId);

        _logger.LogInformation("Running agent in-process for group {GroupName}", request.GroupName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Set up standard IPC directory
            var ipcDir = Path.Combine(_repoRoot, "data", "ipc");
            Directory.CreateDirectory(ipcDir);

            // Set environment variables for the in-process agent
            Environment.SetEnvironmentVariable("HONEYBADGER_IPC_DIR", ipcDir);
            Environment.SetEnvironmentVariable("HONEYBADGER_GROUP", request.GroupName);

            // Optionally set project path for main group
            var projectPath = _options.GetProjectPathForGroup(request.GroupName);
            if (projectPath is not null)
            {
                Environment.SetEnvironmentVariable("HONEYBADGER_PROJECT_MOUNT", projectPath);
                _logger.LogDebug("Project path available for {Group}: {Path}", request.GroupName, projectPath);
            }

            // Use provided tools or create default IpcTools for legacy mode
            IEnumerable<AIFunction> agentTools;
            if (tools is not null)
            {
                _logger.LogDebug("Using provided tools (multi-agent mode)");
                agentTools = tools;
            }
            else
            {
                _logger.LogDebug("Creating default IpcTools (legacy mode)");
                var ipcTools = new IpcTools(ipcDir, request.GroupName,
                    _loggerFactory.CreateLogger<IpcTools>(), request.CorrelationId);
                agentTools = ipcTools.GetAll();
            }

            var orchestrator = new AgentOrchestrator(agentTools,
                _loggerFactory.CreateLogger<AgentOrchestrator>());

            // Run the agent with streaming support
            var response = await orchestrator.RunAsync(request, onStreamChunk, cancellationToken);

            stopwatch.Stop();
            _logger.LogDebug("Agent completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("Agent completed for group {GroupName}", request.GroupName);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Local agent failed for group {GroupName} after {ElapsedMs}ms",
                request.GroupName, stopwatch.ElapsedMilliseconds);
            return new AgentResponse
            {
                Success = false,
                Error = $"Local agent error: {ex.Message}",
                SessionId = request.SessionId
            };
        }
    }
}
