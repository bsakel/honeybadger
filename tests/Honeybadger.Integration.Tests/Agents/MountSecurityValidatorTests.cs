using FluentAssertions;
using Honeybadger.Core.Configuration;
using Honeybadger.Host.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Honeybadger.Integration.Tests.Agents;

public class MountSecurityValidatorTests
{
    private static MountSecurityValidator Create(string allowlistPath = "config/mount-allowlist.json")
    {
        var options = Options.Create(new SecurityOptions
        {
            MountAllowlistPath = allowlistPath,
            ResolveSymlinks = false
        });
        return new MountSecurityValidator(options, NullLogger<MountSecurityValidator>.Instance);
    }

    [Theory]
    [InlineData("groups/main")]
    [InlineData("data/ipc")]
    [InlineData("/app/groups/main")]
    public void AllowedPaths_ReturnTrue(string path)
    {
        var validator = Create("config/mount-allowlist.json");
        validator.IsAllowed(path).Should().BeTrue();
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("secrets.pem")]
    [InlineData("/home/user/.ssh/id_rsa")]
    [InlineData("/root/.aws/credentials")]
    [InlineData("mykey.pfx")]
    public void BlockedPatterns_ReturnFalse(string path)
    {
        var validator = Create("config/mount-allowlist.json");
        validator.IsAllowed(path).Should().BeFalse();
    }

    [Fact]
    public void UnknownPath_ReturnsFalse()
    {
        var validator = Create("config/mount-allowlist.json");
        validator.IsAllowed("/random/path/not/allowed").Should().BeFalse();
    }

    [Fact]
    public void EmptyPath_ReturnsFalse()
    {
        var validator = Create("config/mount-allowlist.json");
        validator.IsAllowed("").Should().BeFalse();
    }

    [Fact]
    public void MissingAllowlistFile_UsesDefaults()
    {
        var validator = Create("nonexistent/path.json");
        // defaults include groups/ and data/
        validator.IsAllowed("groups/test").Should().BeTrue();
    }
}
