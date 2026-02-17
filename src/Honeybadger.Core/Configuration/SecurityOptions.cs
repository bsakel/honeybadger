namespace Honeybadger.Core.Configuration;

public class SecurityOptions
{
    public const string SectionName = "Security";

    public string MountAllowlistPath { get; set; } = "config/mount-allowlist.json";
    public bool ResolveSymlinks { get; set; } = true;
}
