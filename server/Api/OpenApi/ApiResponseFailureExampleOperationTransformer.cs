using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Payment.DomainService.Responses;

namespace Api.OpenApi;

/// <summary>
/// Gives each 4xx/5xx response an explicit example showing <c>success: false</c>.
/// </summary>
/// <remarks>
/// Every controller in this API — Payment, Subscription, PdfGenerator's document conversions —
/// declares both its success and its failure responses as <c>ApiResponse&lt;T&gt;</c> for the same
/// <c>T</c>, so a 200 and its sibling 400 point at the exact same component schema. The OpenAPI
/// document itself carries no example (confirmed by inspecting the generated document: schemas here
/// have no <c>example</c> keyword at all), so Swagger UI and Scalar each synthesize their own
/// generic one from the schema shape when a response has none — and for a bare <c>boolean</c>
/// property with no guidance, that synthesis defaults to <c>true</c>. Since the 200 and 400
/// responses share one schema, both end up showing the identical synthesized example, and a 400 in
/// the docs reads <c>success: true</c> even though the server always returns <c>false</c> there.
/// <para>
/// Attaching an explicit example directly to each failure response (rather than editing the shared
/// schema, which would leak the same override onto the success response too) is the one point of
/// control that lets a 200 and a 400 for the same <c>T</c> show different examples in the docs.
/// Success responses are left alone: their schema-synthesized <c>success: true</c> already matches
/// what the server sends.
/// </para>
/// </remarks>
internal sealed class ApiResponseFailureExampleOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var responseType in context.Description.SupportedResponseTypes)
        {
            if (responseType.StatusCode < 400 || !IsApiResponse(responseType.Type))
            {
                continue;
            }

            var key = responseType.StatusCode.ToString(CultureInfo.InvariantCulture);
            if (!operation.Responses.TryGetValue(key, out var response))
            {
                continue;
            }

            var example = BuildFailureExample();
            foreach (var mediaType in response.Content.Values)
            {
                // Each media type entry gets its own JsonNode instance. They would otherwise share
                // one mutable node across content types, and Microsoft.OpenApi's YAML/JSON writer
                // walks each occurrence independently — a shared instance risks being written out
                // more than once or mutated by a later transformer touching one content type only.
                mediaType.Example = BuildFailureExample();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// True for <c>ApiResponse&lt;T&gt;</c> for any <c>T</c>. Reflection on the declared response
    /// type rather than matching the generated schema's name, so this keeps working if the schema
    /// naming ever changes.
    /// </summary>
    private static bool IsApiResponse(Type? type) =>
        type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(ApiResponse<>);

    /// <summary>
    /// A generic failure envelope. The real <c>code</c>/<c>message</c> vary per endpoint and aren't
    /// worth hand-authoring for every one of them; what matters for the reported problem is that
    /// <c>success</c> reads <c>false</c> here, matching what <see cref="ApiResponse{T}.Fail"/>
    /// actually sends.
    /// </summary>
    private static JsonNode BuildFailureExample() =>
        new JsonObject
        {
            ["success"] = false,
            ["data"] = null,
            ["error"] = new JsonObject
            {
                ["code"] = "example_error_code",
                ["message"] = "Example error message.",
                ["fields"] = null,
                ["traceId"] = "00-0000000000000000000000000000000-0000000000000000-00"
            },
            ["meta"] = new JsonObject
            {
                ["correlationId"] = "00-0000000000000000000000000000000-0000000000000000-00",
                ["timestampUtc"] = "2026-01-01T00:00:00Z",
                ["replayed"] = false
            }
        };
}
