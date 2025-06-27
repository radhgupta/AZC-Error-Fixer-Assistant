using AzcAnalyzerFixer.Core.Interfaces;
using AzcAnalyzerFixer.Infrastructure.Helpers;
using AzcAnalyzerFixer.Infrastructure.Services;
using AzcAnalyzerFixer.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AzcAnalyzerFixer
{
    public static class Startup
    {
        public static ServiceProvider Configure()
        {
            var services = new ServiceCollection();

            services.AddSingleton<ILoggerService, ConsoleLoggerService>();
            services.AddSingleton<FileHelper>(_ => new FileHelper(
                Configuration.AppSettings.MainTspPath,
                Configuration.AppSettings.LogPath));

            services.AddSingleton<IAzcAgentService>(provider =>
            {
                var logger = provider.GetRequiredService<ILoggerService>();
                var fileHelper = provider.GetRequiredService<FileHelper>();
                return new AzcAgentService(Configuration.AppSettings.ProjectEndpoint, Configuration.AppSettings.Model, logger, fileHelper);
            });

            services.AddSingleton<ITypeSpecBuildService>(provider =>
            {
                var logger = provider.GetRequiredService<ILoggerService>();
                return new TypeSpecBuildService(Configuration.AppSettings.WorkspacePath, logger);
            });

            return services.BuildServiceProvider();
        }
    }
}
