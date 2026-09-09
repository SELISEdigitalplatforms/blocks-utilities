using System.Text.Json;
using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.PdfBox;

/// <summary>
/// Parses the single marker-prefixed JSON line PdfBoxNormalizeGeometry writes to stdout.
/// </summary>
public sealed class PdfBoxGeometryReportParser
{
    public const string Marker = "##PDFBOX_GEOMETRY##";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Returns the payload of the LAST marker line, so log4j status output or any other stdout
    /// noise preceding the report is tolerated. Null when the tool emitted no report.
    /// </summary>
    public static string? ExtractPayload(string standardOutput)
    {
        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            return null;
        }

        var lines = standardOutput.Split(["\r\n", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (lines[i].StartsWith(Marker, StringComparison.Ordinal))
            {
                return lines[i][Marker.Length..];
            }
        }

        return null;
    }

    /// <summary>Throws <see cref="JsonException"/> when the payload is malformed.</summary>
    public PdfGeometryNormalizeResult Parse(string payload, IReadOnlyList<string> processWarnings)
    {
        var report = JsonSerializer.Deserialize<GeometryReport>(payload, SerializerOptions)
            ?? throw new JsonException("PDFBox geometry report deserialized to null.");

        return new PdfGeometryNormalizeResult
        {
            Success = true,
            Engine = string.IsNullOrWhiteSpace(report.Engine) ? "pdfbox" : report.Engine,
            PageCount = report.PageCount,
            Signed = report.Signed,
            GeometryChanged = report.Changed,
            Pages = [.. report.Pages.Select(ToPageGeometry)],
            Warnings = [.. report.Warnings.Concat(processWarnings).Distinct()]
        };
    }

    private static PdfPageGeometry ToPageGeometry(GeometryReportPage page)
    {
        return new PdfPageGeometry
        {
            Index = page.Index,
            Rotate = page.Rotate,
            Width = page.Width,
            Height = page.Height,
            MediaBox = page.MediaBox,
            CropBox = page.CropBox,
            RewrittenBoxes = page.RewrittenBoxes,
            IndirectBoxes = page.IndirectBoxes
        };
    }

    private sealed class GeometryReport
    {
        public string Engine { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public bool Signed { get; set; }
        public bool Changed { get; set; }
        public List<GeometryReportPage> Pages { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }

    private sealed class GeometryReportPage
    {
        public int Index { get; set; }
        public int Rotate { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public List<double> MediaBox { get; set; } = [];
        public List<double> CropBox { get; set; } = [];
        public List<string> RewrittenBoxes { get; set; } = [];
        public List<string> IndirectBoxes { get; set; } = [];
    }
}
