namespace Honeybadger.Core.Configuration;

public class HoneybadgerOptions
{
    public AgentOptions Agent { get; set; } = new();
    public Dictionary<string, GroupOptions> Groups { get; set; } = [];
    public DatabaseOptions Database { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();

    /// <summary>Resolve the model for a given group, falling back to the default.</summary>
    public string GetModelForGroup(string groupName)
    {
        if (Groups.TryGetValue(groupName, out var group) && !string.IsNullOrEmpty(group.Model))
            return group.Model;
        return Agent.DefaultModel;
    }

    /// <summary>Get the group that is marked as main (or the first one without a trigger).</summary>
    public string GetMainGroupName()
    {
        foreach (var (name, opts) in Groups)
        {
            if (opts.IsMain || opts.Trigger is null)
                return name;
        }
        return "main";
    }

    /// <summary>Get the project path to mount for a group (if IsMain and ProjectPath is set).</summary>
    public string? GetProjectPathForGroup(string groupName)
    {
        if (Groups.TryGetValue(groupName, out var group) && group.IsMain && !string.IsNullOrEmpty(group.ProjectPath))
            return group.ProjectPath;
        return null;
    }
}
