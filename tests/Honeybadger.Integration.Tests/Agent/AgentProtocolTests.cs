using System.Text.Json;
using FluentAssertions;
using Honeybadger.Core.Models;

namespace Honeybadger.Integration.Tests.Agent;

/// <summary>
/// Tests the agent stdin/stdout protocol parsing logic.
/// Verifies AgentRequest serialization and AgentResponse parsing with sentinels.
/// </summary>
public class AgentProtocolTests
{
    private const string OutputStart = "---HONEYBADGER_OUTPUT_START---";
    private const string OutputEnd = "---HONEYBADGER_OUTPUT_END---";

    [Fact]
    public void AgentRequest_SerializesAndDeserializes()
    {
        var request = new AgentRequest
        {
            MessageId = "msg-1",
            GroupName = "main",
            Content = "Hello agent",
            Model = "claude-sonnet-4.5",
            CopilotCliEndpoint = "localhost:3100"
        };

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<AgentRequest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        deserialized.Should().NotBeNull();
        deserialized!.MessageId.Should().Be("msg-1");
        deserialized.GroupName.Should().Be("main");
        deserialized.Content.Should().Be("Hello agent");
        deserialized.Model.Should().Be("claude-sonnet-4.5");
    }

    [Fact]
    public void ParseSentinelOutput_ExtractsResponseJson()
    {
        var response = new AgentResponse
        {
            Success = true,
            Content = "Hello from agent!",
            SessionId = "sess-123"
        };

        var stdout = $"Some debug output\n{OutputStart}\n{JsonSerializer.Serialize(response)}\n{OutputEnd}\n";

        var parsed = ParseAgentOutput(stdout);

        parsed.Should().NotBeNull();
        parsed!.Success.Should().BeTrue();
        parsed.Content.Should().Be("Hello from agent!");
        parsed.SessionId.Should().Be("sess-123");
    }

    [Fact]
    public void ParseSentinelOutput_ReturnsNull_WhenMarkersAbsent()
    {
        var stdout = "No markers here";
        var parsed = ParseAgentOutput(stdout);
        parsed.Should().BeNull();
    }

    private static AgentResponse? ParseAgentOutput(string stdout)
    {
        var startIdx = stdout.IndexOf(OutputStart, StringComparison.Ordinal);
        var endIdx = stdout.IndexOf(OutputEnd, StringComparison.Ordinal);
        if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx) return null;

        var json = stdout[(startIdx + OutputStart.Length)..endIdx].Trim();
        return JsonSerializer.Deserialize<AgentResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
