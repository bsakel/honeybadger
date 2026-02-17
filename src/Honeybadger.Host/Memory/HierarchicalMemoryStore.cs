using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Memory;

/// <summary>
/// Loads global CLAUDE.md and per-group CLAUDE.md files as agent context.
/// </summary>
public class HierarchicalMemoryStore(string repoRoot, ILogger<HierarchicalMemoryStore> logger)
{
    private readonly string _globalMemoryPath = Path.Combine(repoRoot, "CLAUDE.md");
    private readonly string _groupsRoot = Path.Combine(repoRoot, "groups");

    public string? LoadGlobalMemory()
    {
        if (!File.Exists(_globalMemoryPath)) return null;
        logger.LogDebug("Loading global memory from {Path}", _globalMemoryPath);
        return File.ReadAllText(_globalMemoryPath);
    }

    public string? LoadGroupMemory(string groupName)
    {
        var path = Path.Combine(_groupsRoot, groupName, "CLAUDE.md");
        if (!File.Exists(path)) return null;
        logger.LogDebug("Loading group memory from {Path}", path);
        return File.ReadAllText(path);
    }
}
