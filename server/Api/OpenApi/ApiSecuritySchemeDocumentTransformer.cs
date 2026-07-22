using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Api.OpenApi;

internal sealed class ApiSecuritySchemeDocumentTransformer
    : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(
                StringComparer.Ordinal);

        document.Components.SecuritySchemes[
            OpenApiSecuritySchemeNames.Bearer] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description =
                    "Enter the access token without the Bearer prefix."
            };

        document.Components.SecuritySchemes[
            OpenApiSecuritySchemeNames.BlocksKey] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = "x-blocks-key",
                In = ParameterLocation.Header,
                Description =
                    "Tenant key required by authenticated Blocks endpoints."
            };

        return Task.CompletedTask;
    }
}
