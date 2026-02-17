namespace Honeybadger.Core.Configuration;

public class GroupOptions
{
    public string? Model { get; set; }
    public string? Trigger { get; set; }
    public bool IsMain { get; set; }
    public string? ProjectPath { get; set; }
}
