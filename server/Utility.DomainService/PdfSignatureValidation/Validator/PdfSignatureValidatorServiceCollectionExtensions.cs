using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Utility.DomainService.PdfSignatureValidation.Validator
{
    /// <summary>
    /// Registers the EU DSS validator process and its supervisor.
    /// </summary>
    /// <remarks>
    /// Called only from <c>Worker/Program.cs</c>, for the reason
    /// <c>RegisterPdfIngestionToolchain</c> is: registering the supervisor starts a Java process with
    /// the host, which the Api must never do. The request and status surface
    /// (<c>IPdfSignatureValidationService</c>) is registered for both hosts in
    /// <c>RegisterUtilityServices</c>.
    /// </remarks>
    public static class PdfSignatureValidatorServiceCollectionExtensions
    {
        public static IServiceCollection RegisterPdfSignatureValidator(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.Configure<PdfSignatureValidatorOptions>(configuration.GetSection(PdfSignatureValidatorOptions.SectionName));

            // The supervisor and the gauges read the clock through this. RegisterUtilityServices adds
            // it too; TryAdd means this registration works alone and never replaces a fake a host set.
            services.TryAddSingleton(TimeProvider.System);

            services.AddSingleton<IValidatorChannelFactory, ValidatorProcessChannelFactory>();

            // One instance shared by the supervisor (process counters, the two gauges) and the
            // consumer (job counters), so both report into the same meter.
            services.AddSingleton<PdfSignatureValidationMetrics>();

            // One instance that is both the hosted service that keeps the process alive and the
            // IPdfSignatureValidator the consumer calls; two registrations would be two processes.
            services.AddSingleton<PdfSignatureValidatorSupervisor>();
            services.AddSingleton<IPdfSignatureValidator>(provider => provider.GetRequiredService<PdfSignatureValidatorSupervisor>());
            services.AddHostedService(provider => provider.GetRequiredService<PdfSignatureValidatorSupervisor>());

            return services;
        }
    }
}
