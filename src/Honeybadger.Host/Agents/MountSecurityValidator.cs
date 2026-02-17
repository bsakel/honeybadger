using System.Text.Json;
using Honeybadger.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Honeybadger.Host.Agents;

public class MountSecurityValidator
{
    private readonly SecurityOptions _options;
    private readonly ILogger<MountSecurityValidator> _logger;
    private MountAllowlist? _allowlist;

    public MountSecurityValidator(IOptions<SecurityOptions> options, ILogger<MountSecurityValidator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAllowed(string hostPath)
    {
        if (string.IsNullOrWhiteSpace(hostPath)) return false;

        var allowlist = GetAllowlist();
        var resolved = _options.ResolveSymlinks ? TryResolveSymlinks(hostPath) : hostPath;

        // Normalise separators
        var normalised = resolved.Replace('\\', '/').TrimEnd('/');

        // Check blocked patterns first
        foreach (var pattern in allowlist.BlockedPatterns)
        {
            if (MatchesPattern(normalised, pattern))
            {
                _logger.LogWarning("Mount blocked — matches blocked pattern '{Pattern}': {Path}", pattern, hostPath);
                return false;
            }
        }

        // Check allowed paths — at least one must match (path segment matching)
        foreach (var allowed in allowlist.AllowedPaths)
        {
            var normAllowed = allowed.Replace('\\', '/').TrimEnd('/');

            // Check if allowed path appears as a proper path component:
            // - Starts with "allowed/" (e.g., "groups/main" with allowed "groups")
            // - Equals exactly "allowed" (e.g., "groups" with allowed "groups")
            // - Contains "/allowed/" (e.g., "/app/groups/main" with allowed "groups")
            // - Ends with "/allowed" (e.g., "/app/groups" with allowed "groups")
            if (normalised.StartsWith(normAllowed + "/", StringComparison.OrdinalIgnoreCase) ||
                normalised.Equals(normAllowed, StringComparison.OrdinalIgnoreCase) ||
                normalised.Contains("/" + normAllowed + "/", StringComparison.OrdinalIgnoreCase) ||
                normalised.EndsWith("/" + normAllowed, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Mount allowed via '{Allowed}': {Path}", allowed, hostPath);
                return true;
            }
        }

        _logger.LogWarning("Mount not in allowlist: {Path}", hostPath);
        return false;
    }

    private string TryResolveSymlinks(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                var info = new FileInfo(path);
                // ResolveLinkTarget(true) follows the full symlink chain
                var finalTarget = info.ResolveLinkTarget(returnFinalTarget: true);
                if (finalTarget is not null)
                {
                    _logger.LogDebug("Resolved symlink {Path} -> {Target}", path, finalTarget.FullName);
                    return finalTarget.FullName;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve symlink for {Path}", path);
        }
        return path;
    }

    private static bool MatchesPattern(string path, string pattern)
    {
        // Simple glob matching: leading * is treated as wildcard suffix check
        if (pattern.StartsWith("*."))
        {
            var ext = pattern[1..]; // ".env", ".pem", etc.
            return path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }
        // Trailing slash = directory pattern (should be anywhere in path)
        if (pattern.EndsWith('/'))
        {
            var dirPattern = pattern.TrimEnd('/');
            return path.Contains("/" + dirPattern + "/", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/" + dirPattern, StringComparison.OrdinalIgnoreCase);
        }
        // Filename pattern (e.g., "id_rsa", "credentials")
        return path.Contains("/" + pattern, StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private MountAllowlist GetAllowlist()
    {
        if (_allowlist is not null) return _allowlist;

        if (!File.Exists(_options.MountAllowlistPath))
        {
            _logger.LogWarning("Mount allowlist not found at {Path}, using defaults", _options.MountAllowlistPath);
            _allowlist = MountAllowlist.Default;
            return _allowlist;
        }

        try
        {
            var json = File.ReadAllText(_options.MountAllowlistPath);
            _allowlist = JsonSerializer.Deserialize<MountAllowlist>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? MountAllowlist.Default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load mount allowlist from {Path}", _options.MountAllowlistPath);
            _allowlist = MountAllowlist.Default;
        }

        return _allowlist;
    }
}

internal class MountAllowlist
{
    public List<string> AllowedPaths { get; init; } = [];
    public List<string> BlockedPatterns { get; init; } = [];

    public static MountAllowlist Default => new()
    {
        AllowedPaths = ["groups/", "data/"],
        BlockedPatterns = ["*.env", "*.pem", "*.key", "*.pfx", "*.p12",
                           "id_rsa", "id_ed25519", "credentials", ".ssh/", ".aws/", ".azure/"]
    };
}
