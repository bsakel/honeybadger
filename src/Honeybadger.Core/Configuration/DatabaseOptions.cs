namespace Honeybadger.Core.Configuration;

public class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "Data Source=data/honeybadger.db";
}
