using FluentAssertions;
using Honeybadger.Host.Agents;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honeybadger.Host.Tests.Agents;

public class AgentRegistryTests : IDisposable
{
    private readonly string _tempDir;

    public AgentRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hb-agent-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void LoadFromDirectory_LoadsValidConfigs()
    {
        // Arrange: create temp directory with 2 agent configs
        var configDir = Path.Combine(_tempDir, "config", "agents");
        Directory.CreateDirectory(configDir);

        File.WriteAllText(Path.Combine(configDir, "agent1.json"), """
        {
            "agentId": "agent1",
            "name": "Agent One",
            "description": "Test agent 1",
            "tools": ["tool1", "tool2"]
        }
        """);

        File.WriteAllText(Path.Combine(configDir, "agent2.json"), """
        {
            "agentId": "agent2",
            "name": "Agent Two",
            "description": "Test agent 2",
            "isRouter": true
        }
        """);

        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);

        // Act
        registry.LoadFromDirectory(configDir);

        // Assert
        var agent1 = registry.GetAgent("agent1");
        agent1.Should().NotBeNull();
        agent1!.Name.Should().Be("Agent One");
        agent1.Tools.Should().HaveCount(2);

        var agent2 = registry.GetAgent("agent2");
        agent2.Should().NotBeNull();
        agent2!.IsRouter.Should().BeTrue();

        registry.GetAllAgents().Should().HaveCount(2);
    }

    [Fact]
    public void LoadFromDirectory_SkipsInvalidJson()
    {
        // Arrange
        var configDir = Path.Combine(_tempDir, "config", "agents");
        Directory.CreateDirectory(configDir);

        File.WriteAllText(Path.Combine(configDir, "invalid.json"), "{ invalid json }");
        File.WriteAllText(Path.Combine(configDir, "valid.json"), """
        {
            "agentId": "valid",
            "name": "Valid Agent",
            "description": "Valid config"
        }
        """);

        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);

        // Act
        registry.LoadFromDirectory(configDir);

        // Assert: only valid config should be loaded
        registry.GetAllAgents().Should().HaveCount(1);
        registry.GetAgent("valid").Should().NotBeNull();
    }

    [Fact]
    public void LoadFromDirectory_WarnsMissingAgentId()
    {
        // Arrange
        var configDir = Path.Combine(_tempDir, "config", "agents");
        Directory.CreateDirectory(configDir);

        File.WriteAllText(Path.Combine(configDir, "no-id.json"), """
        {
            "name": "No ID Agent",
            "description": "Missing agentId"
        }
        """);

        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);

        // Act
        registry.LoadFromDirectory(configDir);

        // Assert: config should be skipped
        registry.GetAllAgents().Should().BeEmpty();
    }

    [Fact]
    public void GetRouterAgent_ReturnsSingleRouter()
    {
        // Arrange
        var configDir = Path.Combine(_tempDir, "config", "agents");
        Directory.CreateDirectory(configDir);

        File.WriteAllText(Path.Combine(configDir, "router.json"), """
        {
            "agentId": "router",
            "name": "Router",
            "description": "Router agent",
            "isRouter": true
        }
        """);

        File.WriteAllText(Path.Combine(configDir, "specialist.json"), """
        {
            "agentId": "specialist",
            "name": "Specialist",
            "description": "Specialist agent"
        }
        """);

        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);
        registry.LoadFromDirectory(configDir);

        // Act
        var router = registry.GetRouterAgent();

        // Assert
        router.Should().NotBeNull();
        router!.AgentId.Should().Be("router");
    }

    [Fact]
    public void GetRouterAgent_ReturnsNull_WhenNoRouter()
    {
        // Arrange
        var configDir = Path.Combine(_tempDir, "config", "agents");
        Directory.CreateDirectory(configDir);

        File.WriteAllText(Path.Combine(configDir, "specialist.json"), """
        {
            "agentId": "specialist",
            "name": "Specialist",
            "description": "Specialist agent"
        }
        """);

        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);
        registry.LoadFromDirectory(configDir);

        // Act
        var router = registry.GetRouterAgent();

        // Assert
        router.Should().BeNull();
    }

    [Fact]
    public void GetSpecialistAgents_ExcludesRouter()
    {
        // Arrange
        var configDir = Path.Combine(_tempDir, "config", "agents");
        Directory.CreateDirectory(configDir);

        File.WriteAllText(Path.Combine(configDir, "router.json"), """
        {
            "agentId": "router",
            "name": "Router",
            "description": "Router agent",
            "isRouter": true
        }
        """);

        File.WriteAllText(Path.Combine(configDir, "specialist1.json"), """
        {
            "agentId": "specialist1",
            "name": "Specialist 1",
            "description": "Specialist agent 1"
        }
        """);

        File.WriteAllText(Path.Combine(configDir, "specialist2.json"), """
        {
            "agentId": "specialist2",
            "name": "Specialist 2",
            "description": "Specialist agent 2"
        }
        """);

        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);
        registry.LoadFromDirectory(configDir);

        // Act
        var specialists = registry.GetSpecialistAgents().ToList();

        // Assert
        specialists.Should().HaveCount(2);
        specialists.Should().NotContain(a => a.AgentId == "router");
    }

    [Fact]
    public void GetAgentSummary_FormatsCorrectly()
    {
        // Arrange
        var configDir = Path.Combine(_tempDir, "config", "agents");
        Directory.CreateDirectory(configDir);

        File.WriteAllText(Path.Combine(configDir, "specialist.json"), """
        {
            "agentId": "specialist",
            "name": "Test Specialist",
            "description": "A test specialist agent",
            "capabilities": ["Cap 1", "Cap 2"]
        }
        """);

        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);
        registry.LoadFromDirectory(configDir);

        // Act
        var summary = registry.GetAgentSummary();

        // Assert
        summary.Should().Contain("## Available Specialist Agents");
        summary.Should().Contain("### Test Specialist (`specialist`)");
        summary.Should().Contain("A test specialist agent");
        summary.Should().Contain("Capabilities:");
        summary.Should().Contain("- Cap 1");
        summary.Should().Contain("- Cap 2");
    }

    [Fact]
    public void LoadSoulFile_ReturnsContent()
    {
        // Arrange
        var soulPath = Path.Combine(_tempDir, "test-soul.md");
        File.WriteAllText(soulPath, "# Test Soul\nThis is a test soul file.");

        var registry = new AgentRegistry(NullLogger<AgentRegistry>.Instance);

        // Act
        var content = registry.LoadSoulFile(Path.GetRelativePath(Directory.GetCurrentDirectory(), soulPath));

        // Assert
        content.Should().NotBeNull();
        content.Should().Contain("# Test Soul");
        content.Should().Contain("This is a test soul file.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
