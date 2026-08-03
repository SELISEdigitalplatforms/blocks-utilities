using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Api.OpenApi;

internal sealed class AuthorizedOperationSecurityTransformer
    : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var endpointMetadata =
            context.Description
                .ActionDescriptor
                .EndpointMetadata;

        var allowsAnonymous = endpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();
        var requiresAuthorization = endpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (allowsAnonymous || !requiresAuthorization)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    OpenApiSecuritySchemeNames.Bearer,
                    context.Document)] = [],
                [new OpenApiSecuritySchemeReference(
                    OpenApiSecuritySchemeNames.BlocksKey,
                    context.Document)] = []
            });

        return Task.CompletedTask;
    }
}
