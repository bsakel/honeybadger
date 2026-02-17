using Honeybadger.Core.Configuration;
using FluentAssertions;

namespace Honeybadger.Core.Tests.Configuration;

public class HoneybadgerOptionsTests
{
    [Fact]
    public void GetModelForGroup_ReturnsGroupModel_WhenSet()
    {
        var options = new HoneybadgerOptions
        {
            Agent = new AgentOptions { DefaultModel = "default-model" },
            Groups = new Dictionary<string, GroupOptions>
            {
                ["research"] = new GroupOptions { Model = "group-model" }
            }
        };

        options.GetModelForGroup("research").Should().Be("group-model");
    }

    [Fact]
    public void GetModelForGroup_FallsBackToDefault_WhenGroupHasNoModel()
    {
        var options = new HoneybadgerOptions
        {
            Agent = new AgentOptions { DefaultModel = "default-model" },
            Groups = new Dictionary<string, GroupOptions>
            {
                ["research"] = new GroupOptions { Model = null }
            }
        };

        options.GetModelForGroup("research").Should().Be("default-model");
    }

    [Fact]
    public void GetModelForGroup_FallsBackToDefault_WhenGroupNotFound()
    {
        var options = new HoneybadgerOptions
        {
            Agent = new AgentOptions { DefaultModel = "default-model" }
        };

        options.GetModelForGroup("nonexistent").Should().Be("default-model");
    }

    [Fact]
    public void GetMainGroupName_ReturnsMain_WhenMarkedAsMain()
    {
        var options = new HoneybadgerOptions
        {
            Groups = new Dictionary<string, GroupOptions>
            {
                ["main"] = new GroupOptions { IsMain = true },
                ["other"] = new GroupOptions { IsMain = false }
            }
        };

        options.GetMainGroupName().Should().Be("main");
    }
}
