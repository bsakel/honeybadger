using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Host.Memory;

/// <summary>
/// Loads global AGENT.md and per-group CLAUDE.md files as agent context.
/// Uses ConcurrentDictionary cache + FileSystemWatcher for invalidation.
/// </summary>
public class HierarchicalMemoryStore : IDisposable
{
    private readonly string _globalMemoryPath;
    private readonly string _groupsRoot;
    private readonly ILogger<HierarchicalMemoryStore> _logger;
    private readonly ConcurrentDictionary<string, string?> _cache = new();
    private FileSystemWatcher? _globalWatcher;
    private FileSystemWatcher? _groupsWatcher;

    public HierarchicalMemoryStore(string repoRoot, ILogger<HierarchicalMemoryStore> logger)
    {
        _globalMemoryPath = Path.Combine(repoRoot, "AGENT.md");
        _groupsRoot = Path.Combine(repoRoot, "groups");
        _logger = logger;

        // Setup watchers for cache invalidation
        SetupWatchers(repoRoot);
    }

    private void SetupWatchers(string repoRoot)
    {
        try
        {
            // Watch global AGENT.md
            _globalWatcher = new FileSystemWatcher(repoRoot, "AGENT.md")
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _globalWatcher.Changed += (_, _) => InvalidateCache("global");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to setup global memory watcher");
        }

        try
        {
            // Watch all .md files in groups/* (CLAUDE.md, MEMORY.md, summary.md)
            if (Directory.Exists(_groupsRoot))
            {
                _groupsWatcher = new FileSystemWatcher(_groupsRoot, "*.md")
                {
                    NotifyFilter = NotifyFilters.LastWrite,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _groupsWatcher.Changed += OnGroupFileChanged;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to setup groups memory watcher");
        }
    }

    private void OnGroupFileChanged(object sender, FileSystemEventArgs e)
    {
        // Extract group name and filename from path: groups/{groupName}/{filename}.md
        var relativePath = Path.GetRelativePath(_groupsRoot, e.FullPath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 2) return;

        var groupName = parts[0];
        var fileName = parts[1];

        // Invalidate appropriate cache key based on filename
        var cacheKey = fileName.ToLowerInvariant() switch
        {
            "claude.md" => $"group:{groupName}",
            "memory.md" => $"memory:{groupName}",
            "summary.md" => $"summary:{groupName}",
            _ => null
        };

        if (cacheKey is not null)
            InvalidateCache(cacheKey);
    }

    private void InvalidateCache(string key)
    {
        if (_cache.TryRemove(key, out _))
            _logger.LogDebug("Cache invalidated for {Key}", key);
    }

    public string? LoadGlobalMemory()
    {
        return _cache.GetOrAdd("global", _ =>
        {
            if (!File.Exists(_globalMemoryPath)) return null;
            _logger.LogDebug("Loading global memory from {Path}", _globalMemoryPath);
            return File.ReadAllText(_globalMemoryPath);
        });
    }

    public string? LoadGroupMemory(string groupName)
    {
        return _cache.GetOrAdd($"group:{groupName}", _ =>
        {
            var path = Path.Combine(_groupsRoot, groupName, "CLAUDE.md");
            if (!File.Exists(path)) return null;
            _logger.LogDebug("Loading group memory from {Path}", path);
            return File.ReadAllText(path);
        });
    }

    public string? LoadGroupAgentMemory(string groupName)
    {
        return _cache.GetOrAdd($"memory:{groupName}", _ =>
        {
            var path = Path.Combine(_groupsRoot, groupName, "MEMORY.md");
            if (!File.Exists(path)) return null;
            _logger.LogDebug("Loading group agent memory from {Path}", path);
            return File.ReadAllText(path);
        });
    }

    public string? LoadGroupSummary(string groupName)
    {
        return _cache.GetOrAdd($"summary:{groupName}", _ =>
        {
            var path = Path.Combine(_groupsRoot, groupName, "summary.md");
            if (!File.Exists(path)) return null;
            _logger.LogDebug("Loading group summary from {Path}", path);
            return File.ReadAllText(path);
        });
    }

    public void Dispose()
    {
        _globalWatcher?.Dispose();
        _groupsWatcher?.Dispose();
    }
}
