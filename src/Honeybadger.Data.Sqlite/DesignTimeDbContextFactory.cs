using Honeybadger.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Honeybadger.Data.Sqlite;

/// <summary>Used by EF Core tools (dotnet ef migrations add ...) at design time.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HoneybadgerDbContext>
{
    public HoneybadgerDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HoneybadgerDbContext>();
        optionsBuilder.UseSqlite("Data Source=data/honeybadger.db",
            b => b.MigrationsAssembly("Honeybadger.Data.Sqlite"));
        return new HoneybadgerDbContext(optionsBuilder.Options);
    }
}
