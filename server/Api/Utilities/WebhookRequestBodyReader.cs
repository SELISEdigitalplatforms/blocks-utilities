using System.Buffers;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Api.Utilities;

public sealed class WebhookRequestBodyReader :
    IWebhookRequestBodyReader
{
    private const int BufferSize = 4096;
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public async Task<WebhookRequestBodyReadResult> ReadAsync(
        HttpRequest request,
        int maximumBodyBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (maximumBodyBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBodyBytes));
        }

        if (request.ContentLength > maximumBodyBytes)
        {
            return WebhookRequestBodyReadResult.TooLarge();
        }

        if (request.ContentLength == 0)
        {
            return WebhookRequestBodyReadResult.Malformed();
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            using var body = new MemoryStream(
                request.ContentLength is > 0
                    ? (int)Math.Min(
                        request.ContentLength.Value,
                        maximumBodyBytes)
                    : 0);

            while (true)
            {
                var bytesRead = await request.Body.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken);

                if (bytesRead == 0)
                {
                    break;
                }

                if (body.Length + bytesRead > maximumBodyBytes)
                {
                    return WebhookRequestBodyReadResult.TooLarge();
                }

                await body.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
            }

            if (body.Length == 0 ||
                (request.ContentLength.HasValue &&
                 body.Length != request.ContentLength.Value))
            {
                return WebhookRequestBodyReadResult.Malformed();
            }

            var rawBody = StrictUtf8.GetString(
                body.GetBuffer(),
                0,
                checked((int)body.Length));

            return string.IsNullOrWhiteSpace(rawBody)
                ? WebhookRequestBodyReadResult.Malformed()
                : WebhookRequestBodyReadResult.Success(rawBody);
        }
        catch (BadHttpRequestException)
        {
            return WebhookRequestBodyReadResult.Malformed();
        }
        catch (IOException)
        {
            return WebhookRequestBodyReadResult.Malformed();
        }
        catch (DecoderFallbackException)
        {
            return WebhookRequestBodyReadResult.Malformed();
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return WebhookRequestBodyReadResult.Malformed();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(
                buffer,
                clearArray: true);
        }
    }
}
