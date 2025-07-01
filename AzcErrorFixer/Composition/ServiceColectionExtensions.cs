using Microsoft.Extensions.DependencyInjection;
using AzcAnalyzerFixer.Core.Interfaces;
using AzcAnalyzerFixer.Core.ErrorFixers;
using AzcAnalyzerFixer.Core.Prompting;
using AzcAnalyzerFixer.Infrastructure.Services;
using AzcAnalyzerFixer.Infrastructure.Helpers;
using AzcAnalyzerFixer.Logging;

namespace AzcAnalyzerFixer.Composition
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDomainServices(this IServiceCollection services)
        {
            // Register all IErrorFixerTool implementations
            services.AddSingleton<IErrorFixerTool, Azc0030FixerTool>();
            services.AddSingleton<IErrorFixerTool, Azc0012FixerTool>();
            // TODO: more fixers...

            services.AddSingleton<IPromptBuilder, AzcPromptBuilder>();

            return services;
        }

        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
        {
            // Logging
            services.AddSingleton<ILoggerService, ConsoleLoggerService>();

            // Agent service
            services.AddSingleton<IAzcAgentService>(sp =>
                new AzcAgentService(
                    Configuration.AppSettings.ProjectEndpoint,
                    Configuration.AppSettings.Model,
                    sp.GetRequiredService<ILoggerService>()));

            // Build service
            services.AddSingleton<ITypeSpecBuildService>(sp =>
                new TypeSpecBuildService(
                    Configuration.AppSettings.WorkspacePath,
                    sp.GetRequiredService<ILoggerService>()));

            return services;
        }
    }
}
