using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.PdfBox;

/// <summary>Stdout/stderr handling shared by the PDFBox tool wrappers.</summary>
internal static class PdfBoxProcessOutput
{
    public static string? FirstLine(string text)
    {
        return SplitLines(text).FirstOrDefault();
    }

    public static IReadOnlyList<string> CollectWarnings(ProcessResult result)
    {
        return SplitLines(result.StandardError)
            .Concat(SplitLines(result.StandardOutput))
            .Where(line => line.Contains("warning", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
    }

    public static IReadOnlyList<string> SplitLines(string text)
    {
        return text.Split(
            ["\r\n", "\n"],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
