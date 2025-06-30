using Microsoft.Extensions.DependencyInjection;

namespace AzcAnalyzerFixer.Composition
{
    public static class AzcErrorFixerStartup
    {
        public static ServiceProvider Configure()
        {
            var services = new ServiceCollection();

            // 1) Domain: error‐fixer tools
            services.AddDomainServices();

            // 2) Infrastructure: external clients, helpers, logging
            services.AddInfrastructureServices();

            // 3) (Optional) any cross‐cutting registrations

            return services.BuildServiceProvider();
        }
    }
}
