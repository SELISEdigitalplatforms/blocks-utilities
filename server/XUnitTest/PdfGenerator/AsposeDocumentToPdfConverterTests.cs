using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Utility.DomainService.PdfGenerator;
using Utility.DomainService.PdfGenerator.service;
using Worker.Consumers.PdfGenerator;

namespace XUnitTest.PdfGenerator
{
    public class AsposeDocumentToPdfConverterTests
    {
        private readonly AsposeDocumentToPdfConverter _converter;

        public AsposeDocumentToPdfConverterTests()
        {
            _converter = new AsposeDocumentToPdfConverter(
                Mock.Of<ILogger<AsposeDocumentToPdfConverter>>(),
                new ConfigurationBuilder().Build());
        }

        [Theory]
        [InlineData("contract.docx")]
        [InlineData("contract.doc")]
        [InlineData("LETTER.DOCX")]
        [InlineData("notes.rtf")]
        [InlineData("report.odt")]
        [InlineData("template.dotx")]
        public void IsSupportedDocument_WordProcessingFormats_ReturnsTrue(string fileName)
        {
            _converter.IsSupportedDocument(fileName).Should().BeTrue();
        }

        [Theory]
        [InlineData("already.pdf")]
        [InlineData("sheet.xlsx")]
        [InlineData("photo.png")]
        [InlineData("deck.pptx")]
        [InlineData("archive.zip")]
        public void IsSupportedDocument_NonWordFormats_ReturnsFalse(string fileName)
        {
            _converter.IsSupportedDocument(fileName).Should().BeFalse();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("no-extension")]
        public void IsSupportedDocument_MissingExtension_ReturnsFalse(string fileName)
        {
            // The extension is the only signal available before the file is downloaded, so a name
            // without one has to be rejected rather than optimistically converted.
            _converter.IsSupportedDocument(fileName).Should().BeFalse();
        }

        [Fact]
        public async Task ConvertToPdfAsync_EmptyStream_ReturnsNull()
        {
            using var empty = new MemoryStream();

            var result = await _converter.ConvertToPdfAsync(empty, new DocumentConversionOptions());

            result.Should().BeNull();
        }

        [Fact]
        public async Task ConvertToPdfAsync_MalformedDocx_ReturnsNullRatherThanThrowing()
        {
            // A batch must survive one corrupt member, so a parse failure is a null, not an
            // exception that would abandon the remaining documents. "PK\x03\x04" is the ZIP magic
            // that makes Aspose commit to the OOXML reader, which then fails on the truncated body.
            using var malformed = new MemoryStream(
                new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF });

            var result = await _converter.ConvertToPdfAsync(malformed, new DocumentConversionOptions());

            result.Should().BeNull();
        }

        [Fact]
        public async Task ConvertToPdfAsync_ArbitraryBytes_AreReadAsPlainTextNotRejected()
        {
            // Documents the reason the consumer gates on the file extension before it ever gets
            // here: with no recognisable format signature Aspose falls back to its plain-text
            // reader and cheerfully produces a PDF, so the converter alone will not tell a caller
            // that they handed it something that was never a document.
            using var garbage = new MemoryStream(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

            var result = await _converter.ConvertToPdfAsync(garbage, new DocumentConversionOptions());

            result.Should().NotBeNull();
            await result!.DisposeAsync();
        }

        [Fact]
        public async Task ConvertToPdfAsync_NullStream_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _converter.ConvertToPdfAsync(null!, new DocumentConversionOptions()));
        }

        [Fact]
        public async Task ConvertToPdfAsync_RealDocx_ProducesAPdf()
        {
            using var docx = BuildMinimalDocx();

            using var result = await _converter.ConvertToPdfAsync(docx, new DocumentConversionOptions());

            result.Should().NotBeNull();
            ReadHeader(result!).Should().Be("%PDF", "the output must be a real PDF, not an empty or truncated stream");
            result!.Length.Should().BeGreaterThan(1000);
        }

        [Fact]
        public async Task ConvertToPdfAsync_PdfACompliant_ProducesAPdf()
        {
            using var docx = BuildMinimalDocx();

            using var result = await _converter.ConvertToPdfAsync(
                docx,
                new DocumentConversionOptions { PdfACompliant = true, PreserveFormFields = true });

            // PreserveFormFields is deliberately overridden by PdfACompliant rather than producing
            // a file that claims PDF/A and fails validation.
            result.Should().NotBeNull();
            ReadHeader(result!).Should().Be("%PDF");
        }

        [Fact]
        public async Task ConvertToPdfAsync_DoesNotRequireASeekableSourceStream()
        {
            // Storage hands back a network stream that cannot seek; the converter buffers before
            // parsing precisely so that works.
            using var docx = BuildMinimalDocx();
            using var forwardOnly = new ForwardOnlyStream(docx.ToArray());

            using var result = await _converter.ConvertToPdfAsync(forwardOnly, new DocumentConversionOptions());

            result.Should().NotBeNull();
            ReadHeader(result!).Should().Be("%PDF");
        }

        private static string ReadHeader(Stream stream)
        {
            stream.Position = 0;
            var header = new byte[4];
            stream.ReadExactly(header);
            stream.Position = 0;

            return System.Text.Encoding.ASCII.GetString(header);
        }

        /// <summary>
        /// Builds a valid, minimal OOXML package in memory, so the test exercises the real Word
        /// reader without carrying a binary fixture in the repository.
        /// </summary>
        private static MemoryStream BuildMinimalDocx()
        {
            const string ContentTypes =
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """;

            const string Rels =
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """;

            var paragraphs = string.Concat(Enumerable.Range(1, 40).Select(i =>
                $"""<w:p><w:r><w:t xml:space="preserve">Conversion smoke test, paragraph {i}.</w:t></w:r></w:p>"""));

            var document =
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{paragraphs}</w:body></w:document>
                """;

            var buffer = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(
                buffer,
                System.IO.Compression.ZipArchiveMode.Create,
                leaveOpen: true))
            {
                WriteEntry(archive, "[Content_Types].xml", ContentTypes);
                WriteEntry(archive, "_rels/.rels", Rels);
                WriteEntry(archive, "word/document.xml", document);
            }

            buffer.Position = 0;

            return buffer;
        }

        private static void WriteEntry(System.IO.Compression.ZipArchive archive, string name, string content)
        {
            using var entry = archive.CreateEntry(name).Open();
            using var writer = new StreamWriter(entry);
            writer.Write(content);
        }

        /// <summary>
        /// A stream that reports CanSeek false, standing in for a storage download.
        /// </summary>
        private sealed class ForwardOnlyStream(byte[] data) : Stream
        {
            private readonly MemoryStream _inner = new(data);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            public override void Flush() { }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        [Fact]
        public void DocumentConversionOptions_DefaultsFavourArchivalOutput()
        {
            var options = new DocumentConversionOptions();

            options.EmbedFullFonts.Should().BeTrue();
            options.CompressImages.Should().BeTrue();
            options.PreserveFormFields.Should().BeFalse();
            options.UpdateFields.Should().BeFalse();
            options.PdfACompliant.Should().BeFalse();
        }
    }

    public class ConvertDocumentsToPdfContractTests
    {
        [Theory]
        [InlineData("Q3 Report.docx", "Q3 Report.pdf")]
        [InlineData("contract.doc", "contract.pdf")]
        [InlineData("notes.RTF", "notes.pdf")]
        [InlineData("archive.tar.gz", "archive.tar.pdf")]
        public void ToPdfName_SwapsTheSourceExtension(string sourceName, string expected)
        {
            ConvertDocumentsToPdfConsumer.ToPdfName(sourceName, "file-1").Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ToPdfName_NoUsableSourceName_FallsBackToTheFileId(string? sourceName)
        {
            ConvertDocumentsToPdfConsumer.ToPdfName(sourceName, "file-1").Should().Be("file-1.pdf");
        }

        [Fact]
        public void ConvertCommand_NeedsNothingButTheFileId()
        {
            // The whole point of the contract: name, directory and destination all come from the
            // file's storage record, so a caller supplies one field.
            var command = new ConvertDocumentToPdfCommand { DocumentFileId = "doc-1" };

            command.DocumentFileId.Should().Be("doc-1");
            command.PreserveFormFields.Should().BeFalse();
            command.PdfACompliant.Should().BeFalse();
            command.UpdateFields.Should().BeFalse();
        }

        [Fact]
        public void ConvertDocumentsToPdfRequest_And_Response_ShouldStoreValues()
        {
            var request = new ConvertDocumentsToPdfRequest
            {
                ConvertCommands = new List<ConvertDocumentToPdfCommand>
                {
                    new() { DocumentFileId = "doc-1", PdfACompliant = true }
                }
            };

            // Everything except the commands is optional, so an untouched request is still valid.
            request.ProjectKey.Should().BeNull();
            request.MessageCoRelationId.Should().BeEmpty();
            request.EventReferenceData.Should().BeNull();
            request.ConvertCommands.Should().ContainSingle()
                .Which.DocumentFileId.Should().Be("doc-1");

            var response = new ConvertDocumentsToPdfResponse
            {
                IsSuccess = true,
                MessageCoRelationId = "corr",
                Message = "queued"
            };

            response.IsSuccess.Should().BeTrue();
            response.MessageCoRelationId.Should().Be("corr");
        }
    }
}
