using System.Text.Json;
using Honeybadger.Agent;
using Honeybadger.Agent.Tools.Core;
using Honeybadger.Core.Models;
using Microsoft.Extensions.Logging;

// Agent entry point — runs in-process via LocalAgentRunner.
// Protocol:
//   stdin:  AgentRequest JSON
//   stdout: ---HONEYBADGER_OUTPUT_START---
//           AgentResponse JSON
//           ---HONEYBADGER_OUTPUT_END---

const string OutputStart = "---HONEYBADGER_OUTPUT_START---";
const string OutputEnd = "---HONEYBADGER_OUTPUT_END---";

// Create logger factory that writes to stderr (stdout is reserved for sentinel protocol)
using var loggerFactory = LoggerFactory.Create(b =>
{
    b.SetMinimumLevel(LogLevel.Debug);
    b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
});

var logger = loggerFactory.CreateLogger("Agent.Program");

try
{
    var input = await Console.In.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(input))
    {
        WriteResponse(new AgentResponse { Success = false, Error = "No input received" });
        return 1;
    }

    var request = JsonSerializer.Deserialize<AgentRequest>(input,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (request is null)
    {
        WriteResponse(new AgentResponse { Success = false, Error = "Failed to deserialize request" });
        return 1;
    }

    logger.LogDebug("Agent process received request [Group={Group}, CorrelationId={CorrelationId}]",
        request.GroupName, request.CorrelationId);

    // IPC directory: /workspace/ipc (bind-mounted from host)
    var ipcDir = Environment.GetEnvironmentVariable("HONEYBADGER_IPC_DIR") ?? "/workspace/ipc";
    var groupName = Environment.GetEnvironmentVariable("HONEYBADGER_GROUP") ?? request.GroupName;

    var ipcTools = new IpcTools(ipcDir, groupName,
        loggerFactory.CreateLogger<IpcTools>(), request.CorrelationId);
    var orchestrator = new AgentOrchestrator(ipcTools.GetAll(),
        loggerFactory.CreateLogger<AgentOrchestrator>());
    var response = await orchestrator.RunAsync(request);

    logger.LogDebug("Agent process returning response [Success={Success}, CorrelationId={CorrelationId}]",
        response.Success, request.CorrelationId);
    WriteResponse(response);
    return response.Success ? 0 : 1;
}
catch (Exception ex)
{
    logger.LogError(ex, "Agent process failed");
    WriteResponse(new AgentResponse { Success = false, Error = ex.Message });
    return 1;
}

static void WriteResponse(AgentResponse response)
{
    var json = JsonSerializer.Serialize(response);
    Console.WriteLine(OutputStart);
    Console.WriteLine(json);
    Console.WriteLine(OutputEnd);
}
