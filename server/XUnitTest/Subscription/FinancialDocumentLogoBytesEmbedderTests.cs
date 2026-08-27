using System.Text;
using FluentAssertions;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// The part of logo handling worth testing exhaustively: what bytes are accepted, and what they
/// become. Pure and synchronous, so every case here runs with no storage, no stream, no network --
/// exactly the split <see cref="FinancialDocumentLogoResolver"/>'s own tests could not reach.
/// </summary>
public sealed class FinancialDocumentLogoBytesEmbedderTests
{
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02];

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private static byte[] SvgBytes(string markup = "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>") =>
        Encoding.UTF8.GetBytes(markup);

    [Fact]
    public void Valid_png_bytes_embed_as_a_png_data_uri()
    {
        var dataUri = FinancialDocumentLogoBytesEmbedder.TryEmbed(PngBytes);

        dataUri.Should().StartWith("data:image/png;base64,");
        dataUri.Should().Contain(Convert.ToBase64String(PngBytes));
    }

    [Fact]
    public void Valid_jpeg_bytes_embed_as_a_jpeg_data_uri()
    {
        var dataUri = FinancialDocumentLogoBytesEmbedder.TryEmbed(JpegBytes);

        dataUri.Should().StartWith("data:image/jpeg;base64,");
    }

    [Fact]
    public void Valid_svg_markup_embeds_as_an_svg_data_uri()
    {
        var dataUri = FinancialDocumentLogoBytesEmbedder.TryEmbed(SvgBytes());

        dataUri.Should().StartWith("data:image/svg+xml;base64,");
    }

    [Fact]
    public void An_svg_with_an_xml_prolog_before_the_root_element_still_embeds()
    {
        var dataUri = FinancialDocumentLogoBytesEmbedder.TryEmbed(
            SvgBytes("<?xml version=\"1.0\"?><svg></svg>"));

        dataUri.Should().StartWith("data:image/svg+xml;base64,");
    }

    [Fact]
    public void An_unrecognised_signature_is_refused()
    {
        // GIF's own real magic bytes -- plausible image data, deliberately not on the allow-list.
        var gifBytes = "GIF89a"u8.ToArray();

        FinancialDocumentLogoBytesEmbedder.TryEmbed(gifBytes).Should().BeNull();
    }

    [Fact]
    public void Malformed_svg_that_never_reaches_a_root_element_is_refused()
    {
        var notActuallySvg = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><notsvg/>");

        FinancialDocumentLogoBytesEmbedder.TryEmbed(notActuallySvg).Should().BeNull();
    }

    [Fact]
    public void Empty_bytes_are_refused()
    {
        FinancialDocumentLogoBytesEmbedder.TryEmbed([]).Should().BeNull();
    }

    [Fact]
    public async Task A_stream_within_the_limit_is_read_in_full()
    {
        var content = new byte[100];
        Array.Fill(content, (byte)0x42);

        var bytes = await FinancialDocumentLogoBytesEmbedder.ReadCappedAsync(
            new MemoryStream(content), maxBytes: 100, CancellationToken.None);

        bytes.Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task A_stream_one_byte_over_the_limit_is_rejected_as_oversized()
    {
        var content = new byte[101];

        var bytes = await FinancialDocumentLogoBytesEmbedder.ReadCappedAsync(
            new MemoryStream(content), maxBytes: 100, CancellationToken.None);

        // Not "some bytes and then gave up" -- oversized reports as no bytes at all, so an
        // over-budget stream can never be partially embedded.
        bytes.Should().BeNull();
    }

    [Fact]
    public async Task A_stream_read_larger_than_the_chunk_size_is_still_read_in_full()
    {
        // Bigger than the embedder's own internal chunk size, so this exercises more than one loop
        // iteration rather than being satisfied by a single read.
        var content = new byte[200_000];
        Array.Fill(content, (byte)0x7A);

        var bytes = await FinancialDocumentLogoBytesEmbedder.ReadCappedAsync(
            new MemoryStream(content), maxBytes: 512 * 1024, CancellationToken.None);

        bytes.Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_read_rather_than_silently_returning_partial_bytes()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => FinancialDocumentLogoBytesEmbedder.ReadCappedAsync(
            new MemoryStream(new byte[10]), maxBytes: 100, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
