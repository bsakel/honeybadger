using Honeybadger.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Honeybadger.Data.Sqlite;

public static class SqliteServiceExtensions
{
    public static IServiceCollection AddHoneybadgerSqlite(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HoneybadgerDbContext>(options =>
            options.UseSqlite(connectionString,
                b => b.MigrationsAssembly("Honeybadger.Data.Sqlite")));
        return services;
    }
}
