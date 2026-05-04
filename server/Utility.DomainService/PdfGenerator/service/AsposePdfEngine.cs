using Microsoft.Extensions.Logging;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Drawing;
using System.Text;
using System.Collections;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// PDF engine using PdfSharp for operations
    /// Based on old l2-net-generic-pdf EngineBase implementation
    /// Note: Aspose.Words would be used for HTML to PDF if available
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class AsposePdfEngine : IPdfEngine
    {
        private readonly ILogger<AsposePdfEngine> _logger;

        public AsposePdfEngine(ILogger<AsposePdfEngine> logger)
        {
            _logger = logger;
        }

        public async Task<Stream?> MergePdfsAsync(List<Stream> pdfStreams)
        {
            try
            {
                _logger.LogInformation("AsposePdfEngine: Merging {PdfCount} PDF files using PdfSharp", pdfStreams.Count);

                if (pdfStreams == null || pdfStreams.Count == 0)
                {
                    _logger.LogError("AsposePdfEngine: No PDF streams to merge");
                    return null;
                }

                if (pdfStreams.Count == 1)
                {
                    _logger.LogInformation("AsposePdfEngine: Only one PDF, returning as-is");
                    return pdfStreams[0];
                }

                // Based on old WkHtmlToPdfEngine.MergePdf implementation
                using (var ms = new MemoryStream())
                {
                    await pdfStreams[0].CopyToAsync(ms);
                    ms.Seek(0, SeekOrigin.Begin);

                    using (PdfDocument doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import))
                    {
                        for (int i = 1; i < pdfStreams.Count; i++)
                        {
                            using (var docMs = new MemoryStream())
                            {
                                await pdfStreams[i].CopyToAsync(docMs);
                                docMs.Position = 0;

                                try
                                {
                                    using (PdfDocument document = PdfReader.Open(docMs, PdfDocumentOpenMode.Import))
                                    {
                                        for (int j = 0; j < document.Pages.Count; j++)
                                        {
                                            doc.AddPage(document.Pages[j]);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "AsposePdfEngine: Failed to add PDF at index {Index} to merge", i);
                                    // Continue with other PDFs
                                }
                            }
                        }

                        int pageCount = doc.Pages.Count;
                    _logger.LogInformation("AsposePdfEngine: Merged PDF has {PageCount} pages", pageCount);

                        Stream outputStream = new MemoryStream();
                        doc.Save(outputStream);
                        outputStream.Position = 0;

                        return await Task.FromResult(outputStream);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AsposePdfEngine: Error merging PDFs");
                return null;
            }
        }

        public async Task<Stream?> ConvertHtmlToPdfAsync(string htmlContent, PdfGenerationOptions options)
        {
            try
            {
                _logger.LogInformation("AsposePdfEngine: Converting HTML to PDF using Aspose.Words");
                
                var htmlLoadOptions = new Aspose.Words.HtmlLoadOptions
                {
                    LoadFormat = Aspose.Words.LoadFormat.Html
                };
                
                var htmlBytes = Encoding.UTF8.GetBytes(htmlContent);
                using (var htmlMemoryStream = new MemoryStream(htmlBytes))
                {
                    var document = new Aspose.Words.Document(htmlMemoryStream, htmlLoadOptions);
                    
                    // Setup fonts for Linux
                    var fontPath = "/app/fonts";
                    if (Directory.Exists(fontPath))
                    {
                        var fontSources = new ArrayList(Aspose.Words.Fonts.FontSettings.DefaultInstance.GetFontsSources());
                        var folderFontSource = new Aspose.Words.Fonts.FolderFontSource(fontPath, true);
                        fontSources.Add(folderFontSource);
                        var updatedFontSources = (Aspose.Words.Fonts.FontSourceBase[])fontSources.ToArray(typeof(Aspose.Words.Fonts.FontSourceBase));
                        Aspose.Words.Fonts.FontSettings.DefaultInstance.SetFontsSources(updatedFontSources);
                    }
                    
                    // Apply profile settings
                    if (options.Profile != null)
                    {
                        foreach (var sec in document.Sections.OfType<Aspose.Words.Section>())
                        {
                            if (!string.IsNullOrEmpty(options.Profile.MarginRight))
                                sec.PageSetup.RightMargin = double.Parse(options.Profile.MarginRight);
                            if (!string.IsNullOrEmpty(options.Profile.MarginLeft))
                                sec.PageSetup.LeftMargin = double.Parse(options.Profile.MarginLeft);
                            if (!string.IsNullOrEmpty(options.Profile.HeaderSpacing))
                                sec.PageSetup.TopMargin = double.Parse(options.Profile.HeaderSpacing);
                            if (!string.IsNullOrEmpty(options.Profile.FooterSpacing))
                                sec.PageSetup.BottomMargin = double.Parse(options.Profile.FooterSpacing);
                        }
                    }
                    
                    // Add footers first (order matters)
                    if (!string.IsNullOrEmpty(options.FooterHtml))
                    {
                        _logger.LogInformation("AsposePdfEngine: Adding footer");
                        AddFooters(document, options.FooterHtml, 0);
                    }
                    
                    // Add page numbers if enabled
                    if (options.IsPageNumberEnabled)
                    {
                        _logger.LogInformation("AsposePdfEngine: Adding page numbers");
                        AddPageNumbers(document);
                    }
                    
                    // Add headers
                    if (!string.IsNullOrEmpty(options.HeaderHtml))
                    {
                        _logger.LogInformation("AsposePdfEngine: Adding header");
                        AddHeaders(document, options.HeaderHtml, 0);
                    }
                    
                    // Apply formatting options
                    if (!options.UseFormatting)
                    {
                        CheckboxStyler(document);
                    }
                    
                    // Resize images
                    ResizeAllTheImages(document);
                    
                    // Save to PDF
                    var outputStream = new MemoryStream();
                    document.Save(outputStream, Aspose.Words.SaveFormat.Pdf);
                    outputStream.Position = 0;
                    
                _logger.LogInformation("AsposePdfEngine: Successfully converted HTML to PDF, size={PdfSize} bytes", outputStream.Length);
                    return await Task.FromResult<Stream?>(outputStream);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AsposePdfEngine: Error converting HTML to PDF");
                return null;
            }
        }
        
        private static void AddHeaders(Aspose.Words.Document document, string headerHtml, int startIndex)
        {
            var builder = new Aspose.Words.DocumentBuilder(document);
            var hfList = new ArrayList();

            foreach (var section in document.Sections.OfType<Aspose.Words.Section>())
            {
                var header = section.HeadersFooters[Aspose.Words.HeaderFooterType.HeaderPrimary];

                if (header == null)
                {
                    header = new Aspose.Words.HeaderFooter(document, Aspose.Words.HeaderFooterType.HeaderPrimary);
                    section.HeadersFooters.Add(header);
                }

                builder.MoveToSection(document.Sections.IndexOf(section));
                builder.MoveToHeaderFooter(Aspose.Words.HeaderFooterType.HeaderPrimary);
                builder.InsertHtml(headerHtml);

            foreach (var hf in section.HeadersFooters.OfType<Aspose.Words.HeaderFooter>())
            {
                if (hf.HeaderFooterType == Aspose.Words.HeaderFooterType.HeaderPrimary)
                {
                    hfList.Add(hf);
                }
            }
            }

            if (startIndex < hfList.Count)
            {
                for (int i = 0; i < startIndex; i++)
                {
                    (hfList[i] as Aspose.Words.HeaderFooter)?.Remove();
                }
            }
        }

        private static void AddFooters(Aspose.Words.Document document, string footerHtml, int startIndex)
        {
            var builder = new Aspose.Words.DocumentBuilder(document);
            var hfList = new ArrayList();

            foreach (var section in document.Sections.OfType<Aspose.Words.Section>())
            {
                var footer = section.HeadersFooters[Aspose.Words.HeaderFooterType.FooterPrimary];

                if (footer == null)
                {
                    footer = new Aspose.Words.HeaderFooter(document, Aspose.Words.HeaderFooterType.FooterPrimary);
                    section.HeadersFooters.Add(footer);
                }

                builder.MoveToSection(document.Sections.IndexOf(section));
                builder.MoveToHeaderFooter(Aspose.Words.HeaderFooterType.FooterPrimary);
                builder.InsertHtml(footerHtml);

                foreach (var hf in section.HeadersFooters.OfType<Aspose.Words.HeaderFooter>())
                {
                    if (hf.HeaderFooterType == Aspose.Words.HeaderFooterType.FooterPrimary)
                    {
                        hfList.Add(hf);
                    }
                }
            }

            if (startIndex <= hfList.Count)
            {
                for (int i = 0; i < startIndex; i++)
                {
                    (hfList[i] as Aspose.Words.HeaderFooter)?.Remove();
                }
            }
        }

        private static void AddPageNumbers(Aspose.Words.Document document)
        {
            var builder = new Aspose.Words.DocumentBuilder(document);

            foreach (var sec in document.Sections.OfType<Aspose.Words.Section>())
            {
                sec.PageSetup.TopMargin = 100;
                sec.PageSetup.BottomMargin = 100;

                builder.MoveToHeaderFooter(Aspose.Words.HeaderFooterType.FooterPrimary);
                builder.ParagraphFormat.Alignment = Aspose.Words.ParagraphAlignment.Right;
                builder.InsertField("PAGE", "");
                builder.Write("  of  ");
                builder.InsertField("NUMPAGES", "");
            }
        }

        private static void CheckboxStyler(Aspose.Words.Document document)
        {
            foreach (var singleTable in document.FirstSection.Body.Tables.OfType<Aspose.Words.Tables.Table>())
            {
                var fields = document.GetChildNodes(Aspose.Words.NodeType.FormField, true);
                foreach (Aspose.Words.Fields.FormField field in fields)
                {
                    if (field.Type == Aspose.Words.Fields.FieldType.FieldFormCheckBox)
                    {
                        field.CheckBoxSize = 9.0;
                    }
                }
            }
        }

        private static void ResizeAllTheImages(Aspose.Words.Document document)
        {
            var shapes = document.GetChildNodes(Aspose.Words.NodeType.Shape, true);
            foreach (var shape in shapes.OfType<Aspose.Words.Drawing.Shape>())
            {
                if (shape.ShapeType == Aspose.Words.Drawing.ShapeType.Image && shape.Width > 500.0f)
                {
                    shape.Width += ((500 - shape.Width) >= 0 ? 0 : (500 - shape.Width));
                }
            }
        }

        public async Task<string?> ExtractTextFromPdfAsync(Stream pdfStream)
        {
            try
            {
                _logger.LogInformation("AsposePdfEngine: Extracting text from PDF using PdfPig");
                
                var builder = new StringBuilder();
                using (var ms = new MemoryStream())
                {
                    await pdfStream.CopyToAsync(ms);
                    ms.Position = 0;
                    
                    using (var document = UglyToad.PdfPig.PdfDocument.Open(ms.ToArray()))
                    {
                        foreach (var page in document.GetPages())
                        {
                            var words = page.GetWords();
                            var text = string.Join(" ", words);
                            builder.Append(text);
                            builder.Append(" "); // Add space between pages
                        }
                    }
                    
                    var extractedText = builder.ToString();
                _logger.LogInformation("AsposePdfEngine: Extracted {CharCount} characters from PDF", extractedText.Length);
                    return await Task.FromResult(extractedText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AsposePdfEngine: Error extracting text from PDF");
                return null;
            }
        }

        public async Task<Stream?> FixPdfAsync(Stream pdfStream)
        {
            try
            {
                _logger.LogInformation("AsposePdfEngine: Fixing/repairing PDF using iTextSharp");
                
                var document = new iTextSharp.text.Document();
                var ms = new MemoryStream();
                ms.Seek(0, SeekOrigin.Begin);
                
                var writer = new iTextSharp.text.pdf.PdfCopy(document, ms);
                if (writer == null)
                {
                    _logger.LogError("AsposePdfEngine: Failed to create PdfCopy writer");
                    return null;
                }
                
                document.Open();
                
                int totalPages = 0;
                using (var ms2 = new MemoryStream())
                {
                    pdfStream.CopyTo(ms2);
                    ms2.Seek(0, SeekOrigin.Begin);
                    
                    var reader = new iTextSharp.text.pdf.PdfReader(ms2);
                    reader.ConsolidateNamedDestinations();
                    
                    for (int i = 1; i <= reader.NumberOfPages; i++)
                    {
                        var page = writer.GetImportedPage(reader, i);
                        writer.AddPage(page);
                        totalPages++;
                    }
                    
                    var form = reader.AcroForm;
                    if (form != null)
                    {
                        writer.CopyAcroForm(reader);
                    }
                    
                    reader.Close();
                }
                
                writer.Close();
                document.Close();
                
                ms.Position = 0;
                
                _logger.LogInformation("AsposePdfEngine: Successfully repaired PDF with {TotalPages} pages, size={PdfSize} bytes", totalPages, ms.Length);
                return await Task.FromResult<Stream?>(ms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AsposePdfEngine: Error fixing PDF");
                return null;
            }
        }

        public async Task<Stream?> StampImageToPdfAsync(Stream pdfStream, Stream imageStream, ImageStampOptions options)
        {
            _logger.LogInformation("AsposePdfEngine: Stamping image at ({X}, {Y}) using PdfSharp", options.XPosition, options.YPosition);

            return await ProcessPdfPages(
                pdfStream,
                options.PageNumbers,
                (gfx, page, pageNumber) =>
                {
                    DrawImage(
                        gfx: gfx,
                        imgStream: imageStream,
                        x: (float)options.XPosition,
                        y: (float)options.YPosition,
                        width: (float)options.Width,
                        height: (float)options.Height);
                },
                "image stamping");
        }

        /// <summary>
        /// Opens a PDF document and processes specified pages with a callback action
        /// </summary>
        private async Task<Stream?> ProcessPdfPages(
            Stream pdfStream, 
            List<int>? pageNumbers, 
            Action<XGraphics, PdfPage, int> pageAction,
            string operationName)
        {
            try
            {
                PdfDocument document = PdfReader.Open(stream: pdfStream, openmode: PdfDocumentOpenMode.Modify);

                var targetPages = pageNumbers ?? Enumerable.Range(1, document.Pages.Count).ToList();

                foreach (var pageNumber in targetPages)
                {
                    if (pageNumber <= 0 || pageNumber > document.Pages.Count)
                    {
                        _logger.LogWarning("AsposePdfEngine: Page number {PageNumber} out of range, skipping", pageNumber);
                        continue;
                    }

                    PdfPage page = document.Pages[pageNumber - 1];
                    XGraphics gfx = XGraphics.FromPdfPage(page);

                    pageAction(gfx, page, pageNumber);
                }

                MemoryStream ms = new();
                document.Save(ms);
                ms.Position = 0;

                _logger.LogInformation("AsposePdfEngine: Successfully completed {OperationName}, size={PdfSize} bytes", operationName, ms.Length);
                return await Task.FromResult<Stream?>(ms);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AsposePdfEngine: Error during {OperationName}", operationName);
                return null;
            }
        }

        private static void DrawImage(XGraphics gfx, Stream imgStream, float x, float y, float width, float height)
        {
            XImage image = XImage.FromStream(() => imgStream);

            double ratioWidth = (double)width / image.PixelWidth;
            double ratioHeight = (double)height / image.PixelHeight;
            double ratio = ratioWidth < ratioHeight ? ratioWidth : ratioHeight;

            var xAxis = Convert.ToInt32((width - (image.PixelWidth * ratio)) / 2);
            var yAxis = Convert.ToInt32((height - (image.PixelHeight * ratio)) / 2);

            x += xAxis;
            y += yAxis;
            x *= 0.75f;
            y *= 0.75f;

            var finalWidth = image.PixelWidth * ratio * 0.75;
            var finalHeight = image.PixelHeight * ratio * 0.75;

            gfx.DrawImage(image, x, y, finalWidth, finalHeight);
        }

        public async Task<Stream?> StampTextToPdfAsync(Stream pdfStream, TextStampOptions options)
        {
            _logger.LogInformation("AsposePdfEngine: Stamping text '{Text}' at ({X}, {Y}) using PdfSharp", options.Text, options.XPosition, options.YPosition);

            return await ProcessPdfPages(
                pdfStream,
                options.PageNumbers,
                (gfx, page, pageNumber) =>
                {
                    DrawText(
                        gfx: gfx,
                        page: page,
                        text: options.Text,
                        font: options.FontName ?? "Calibri",
                        x: (float)options.XPosition,
                        y: (float)options.YPosition,
                        width: 500,
                        height: 100);
                },
                "text stamping");
        }
        
        private static void DrawText(XGraphics gfx, PdfPage page, string text, string font, float x, float y, float width, float height)
        {
            try
            {
                using var container = new HtmlRendererCore.PdfSharp.HtmlContainer();
                var pageSize = new XSize(width: page.Width, height: page.Height);
                
                using (var measure = XGraphics.CreateMeasureContext(
                    size: pageSize,
                    pageUnit: XGraphicsUnit.Point,
                    pageDirection: XPageDirection.Downwards))
                {
                    x *= 0.75f;
                    y *= 0.75f;
                    
                    container.Location = new XPoint(x: x, y: y);
                    container.MaxSize = new XSize(width: width, height: height);
                    container.PageSize = pageSize;
                    
                    string formattedValue = $"<div style=\"font-family: {font};\">{text}</div>";
                    container.SetHtml(htmlSource: formattedValue);
                    container.PerformLayout(measure);
                }
                
                container.PerformPaint(gfx);
            }
            catch (Exception)
            {
                // Fallback to direct XFont drawing when HtmlRendererCore fails (e.g. missing fonts on Linux)
                var xFont = new XFont(font, 12, XFontStyle.Regular);
                gfx.DrawString(text, xFont, XBrushes.Black, new XPoint(x * 0.75, y * 0.75));
            }
        }
    }
}

