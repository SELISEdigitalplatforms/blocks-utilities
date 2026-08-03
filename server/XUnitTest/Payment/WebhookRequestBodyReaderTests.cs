using System.Text;
using Api.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Payment;

public sealed class WebhookRequestBodyReaderTests
{
    private readonly WebhookRequestBodyReader _reader = new();

    [Fact]
    public async Task Read_returns_the_complete_request_body()
    {
        const string json = """{"notificationItems":[]}""";
        var request = CreateRequest(
            new MemoryStream(Encoding.UTF8.GetBytes(json)),
            Encoding.UTF8.GetByteCount(json));

        var result = await _reader.ReadAsync(
            request,
            maximumBodyBytes: 1024,
            CancellationToken.None);

        result.Status.Should().Be(
            WebhookRequestBodyReadStatus.Success);
        result.RawBody.Should().Be(json);
    }

    [Fact]
    public async Task Read_rejects_a_body_shorter_than_its_content_length()
    {
        const string incompleteJson = """{"notificationItems":""";
        var bytes = Encoding.UTF8.GetBytes(incompleteJson);
        var request = CreateRequest(
            new MemoryStream(bytes),
            bytes.Length + 10);

        var result = await _reader.ReadAsync(
            request,
            maximumBodyBytes: 1024,
            CancellationToken.None);

        result.Status.Should().Be(
            WebhookRequestBodyReadStatus.Malformed);
    }

    [Fact]
    public async Task Read_converts_kestrel_unexpected_end_into_a_malformed_result()
    {
        var request = CreateRequest(
            new UnexpectedEndStream(),
            contentLength: 100);

        var result = await _reader.ReadAsync(
            request,
            maximumBodyBytes: 1024,
            CancellationToken.None);

        result.Status.Should().Be(
            WebhookRequestBodyReadStatus.Malformed);
    }

    [Fact]
    public async Task Read_bounds_chunked_request_memory()
    {
        var request = CreateRequest(
            new MemoryStream(new byte[1025]),
            contentLength: null);

        var result = await _reader.ReadAsync(
            request,
            maximumBodyBytes: 1024,
            CancellationToken.None);

        result.Status.Should().Be(
            WebhookRequestBodyReadStatus.TooLarge);
    }

    [Fact]
    public async Task Read_rejects_invalid_utf8()
    {
        byte[] invalidUtf8 = [0xC3, 0x28];
        var request = CreateRequest(
            new MemoryStream(invalidUtf8),
            invalidUtf8.Length);

        var result = await _reader.ReadAsync(
            request,
            maximumBodyBytes: 1024,
            CancellationToken.None);

        result.Status.Should().Be(
            WebhookRequestBodyReadStatus.Malformed);
    }

    private static HttpRequest CreateRequest(
        Stream body,
        long? contentLength)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = body;
        context.Request.ContentLength = contentLength;

        return context.Request;
    }

    private sealed class UnexpectedEndStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(
                new BadHttpRequestException(
                    "Unexpected end of request content.",
                    StatusCodes.Status400BadRequest));
    }
}
