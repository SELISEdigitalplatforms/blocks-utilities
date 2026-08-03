using PdfSharp.Pdf;
using PdfSharp.Drawing;
using Microsoft.Extensions.Logging;
using Moq;
using PdfSharp.Pdf.IO;
using System.IO.Compression;
using Utility.DomainService.PdfGenerator.service;

namespace XUnitTest.PdfGenerator
{
    internal static class TestPdfFactory
    {
        public static MemoryStream CreatePdf(int pageCount = 1)
        {
            var doc = new PdfDocument();

            for (int i = 0; i < pageCount; i++)
            {
                doc.AddPage();
            }

            var ms = new MemoryStream();
            doc.Save(ms);
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// Creates a valid PNG image in memory without System.Drawing (cross-platform).
        /// </summary>
        public static MemoryStream CreatePngImage(int width = 100, int height = 50)
        {
            var ms = new MemoryStream();

            // PNG signature
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            // IHDR chunk
            var ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, width);
            WriteBigEndian(ihdr, 4, height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 2;  // color type RGB
            WriteChunk(ms, "IHDR", ihdr);

            // IDAT chunk — raw scanlines: filter byte (0) + width*3 RGB bytes per row
            var rawData = new byte[height * (1 + width * 3)];
            // All zeros = black pixels with None filter — valid PNG data

            WriteChunk(ms, "IDAT", CompressZlib(rawData));

            // IEND chunk
            WriteChunk(ms, "IEND", Array.Empty<byte>());

            ms.Position = 0;
            return ms;
        }

        private static void WriteBigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset]     = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static byte[] CompressZlib(byte[] data)
        {
            using var output = new MemoryStream();
            // zlib header
            output.WriteByte(0x78);
            output.WriteByte(0x01);

            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(data, 0, data.Length);
            }

            // Adler-32 checksum (big-endian)
            uint a = 1, b = 0;
            foreach (byte byt in data)
            {
                a = (a + byt) % 65521;
                b = (b + a) % 65521;
            }
            uint adler = (b << 16) | a;
            output.WriteByte((byte)(adler >> 24));
            output.WriteByte((byte)(adler >> 16));
            output.WriteByte((byte)(adler >> 8));
            output.WriteByte((byte)adler);

            return output.ToArray();
        }

        private static void WriteChunk(MemoryStream ms, string type, byte[] data)
        {
            int len = data.Length;
            ms.WriteByte((byte)(len >> 24));
            ms.WriteByte((byte)(len >> 16));
            ms.WriteByte((byte)(len >> 8));
            ms.WriteByte((byte)len);

            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            ms.Write(typeBytes, 0, typeBytes.Length);
            ms.Write(data, 0, data.Length);

            // CRC32 over type + data
            var crcInput = new byte[typeBytes.Length + data.Length];
            Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
            Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
            uint crc = Crc32(crcInput);
            ms.WriteByte((byte)(crc >> 24));
            ms.WriteByte((byte)(crc >> 16));
            ms.WriteByte((byte)(crc >> 8));
            ms.WriteByte((byte)crc);
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc ^ 0xFFFFFFFF;
        }
    }

    public class PdfSharpCoreEngineTests
    {
        private readonly PdfSharpCoreEngine _engine;

        public PdfSharpCoreEngineTests()
        {
            var logger = new Mock<ILogger<PdfSharpCoreEngine>>();
            _engine = new PdfSharpCoreEngine(logger.Object);
        }


        [Fact]
        public async Task MergePdfsAsync_MergesMultiplePdfs()
        {
            var pdf1 = TestPdfFactory.CreatePdf(1);
            var pdf2 = TestPdfFactory.CreatePdf(2);

            var result = await _engine.MergePdfsAsync(new List<Stream> { pdf1, pdf2 });

            Assert.NotNull(result);
            Assert.True(result!.Length > 0);

            var merged = PdfSharp.Pdf.IO.PdfReader.Open(result, PdfDocumentOpenMode.ReadOnly);
            Assert.Equal(3, merged.PageCount);
        }

        [Fact]
        public async Task MergePdfsAsync_ReturnsNull_WhenNoStreams()
        {
            var result = await _engine.MergePdfsAsync(new List<Stream>());

            Assert.Null(result);
        }

        [Fact]
        public async Task MergePdfsAsync_ReturnsSamePdf_WhenSingleStream()
        {
            var pdf = TestPdfFactory.CreatePdf(2);

            var result = await _engine.MergePdfsAsync(new List<Stream> { pdf });

            Assert.NotNull(result);

            var doc = PdfSharp.Pdf.IO.PdfReader.Open(result!, PdfDocumentOpenMode.ReadOnly);
            Assert.Equal(2, doc.PageCount);
        }

        [Fact]
        public async Task FixPdfAsync_RepairsPdf()
        {
            var pdf = TestPdfFactory.CreatePdf(2);

            var result = await _engine.FixPdfAsync(pdf);

            Assert.NotNull(result);
            Assert.True(result!.Length > 0);

            var doc = PdfSharp.Pdf.IO.PdfReader.Open(result, PdfDocumentOpenMode.ReadOnly);
            Assert.Equal(2, doc.PageCount);
        }

        [Fact]
        public async Task ConvertHtmlToPdfAsync_ReturnsNull()
        {
            var result = await _engine.ConvertHtmlToPdfAsync("<h1>Hello</h1>", new PdfGenerationOptions());

            Assert.Null(result);
        }

        [Fact]
        public async Task ExtractTextFromPdfAsync_ReturnsNull()
        {
            var pdf = TestPdfFactory.CreatePdf();

            var result = await _engine.ExtractTextFromPdfAsync(pdf);

            Assert.Null(result);
        }

        [Fact]
        public async Task StampImageToPdfAsync_StampsImage()
        {
            var pdf = TestPdfFactory.CreatePdf(1);
            var image = TestPdfFactory.CreatePngImage();

            var options = new ImageStampOptions
            {
                XPosition = 50,
                YPosition = 50,
                Width = 100,
                Height = 50
            };

            var result = await _engine.StampImageToPdfAsync(pdf, image, options);

            Assert.NotNull(result);
            Assert.True(result!.Length > 0);
        }

        [Fact]
        public async Task StampTextToPdfAsync_StampsText()
        {
            var pdf = TestPdfFactory.CreatePdf(1);

            var options = new TextStampOptions
            {
                Text = "Hello PDF",
                XPosition = 50,
                YPosition = 50,
                FontSize = 14,
                IsBold = true
            };

            var result = await _engine.StampTextToPdfAsync(pdf, options);

            Assert.NotNull(result);
            Assert.True(result!.Length > 0);
        }

    }
}
