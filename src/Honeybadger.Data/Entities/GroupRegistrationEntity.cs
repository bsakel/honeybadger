namespace Honeybadger.Data.Entities;

public class GroupRegistrationEntity
{
    public int Id { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string? TriggerPattern { get; set; }
    public bool IsMain { get; set; }
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
}
