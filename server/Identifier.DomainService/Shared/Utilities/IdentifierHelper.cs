using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using System.Text;

namespace DomainService.Shared
{
    public static class IdentifierHelper
    {
        public static bool BeAValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uriResult) &&
                   (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        public static string ExtractMainDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return string.Empty;

            domain = domain.Replace("http://", string.Empty)
                 .Replace("https://", string.Empty);

            var parts = domain.Split('.');
            if (parts.Length < 2)
                return domain;

            return string.Join('.', parts.TakeLast(2));
        }

        public static string EnvironmentMapper(string env) =>
        env switch
        {
            "dev" => "d",
            "test" => "t",
            "stg" => "s",
            "iat" => "i",
            "uat" => "u",
            "prod-shadow" => "h",
            "pre-prod" => "r",
            "prod" => "p",
            _ => "n"
        };

        public static (string? Controller, string? Action) GetControllerAction(HttpContext httpContext)
        {
            var endpoint = httpContext.GetEndpoint();
            if (endpoint == null)
                return (null, null);

            var descriptor = endpoint.Metadata
                .GetMetadata<ControllerActionDescriptor>();

            return (
                descriptor?.ControllerName?.ToLowerInvariant(),
                descriptor?.ActionName?.ToLowerInvariant()
            );
        }

        public static bool IsSupportedEnvironment(string env)
        {
            var supportedEnvironments = new HashSet<string>
            {
                "dev", "test", "stg", "iat", "uat", "prod-shadow", "pre-prod", "prod"
            };
            return supportedEnvironments.Contains(env);
        }
        public static bool IsRabbitMq(string connectionString)
        {
            if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
            {
                return uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase) ||
                       uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}