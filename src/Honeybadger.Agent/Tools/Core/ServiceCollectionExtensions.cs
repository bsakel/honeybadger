using Honeybadger.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honeybadger.Agent.Tools.Core;

public static class CoreToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in IPC and delegation tools as an IToolProvider.
    /// Call this from Program.cs to opt in to core tool support.
    /// </summary>
    public static IServiceCollection AddCoreTools(this IServiceCollection services, string ipcDirectory)
    {
        services.AddSingleton<IToolProvider>(sp =>
            new CoreToolProvider(ipcDirectory, sp.GetRequiredService<ILoggerFactory>()));
        return services;
    }
}
