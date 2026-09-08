using System.Xml.Linq;
using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.VeraPdf;

public sealed class VeraPdfXmlParser
{
    public PdfAValidationResult Parse(string rawReport, bool processSuccess, string? processError)
    {
        var document = XDocument.Parse(rawReport);
        var report = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.Equals("validationReport", StringComparison.OrdinalIgnoreCase));

        var isCompliant = ReadBool(report, "isCompliant")
            ?? ReadBool(document.Root, "isCompliant")
            ?? false;

        var profileName = ReadAttribute(report, "profileName")
            ?? ReadAttribute(report, "flavour")
            ?? ReadElementValue(report, "profileName")
            ?? ReadElementValue(report, "flavour")
            ?? ReadElementValue(document.Root, "profileName")
            ?? ReadElementValue(document.Root, "flavour");

        var failedChecks = document
            .Descendants()
            .Where(IsFailedCheckNode)
            .Select(DescribeFailure)
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();

        var errors = new List<string>();
        if (!processSuccess && !string.IsNullOrWhiteSpace(processError))
        {
            errors.Add(processError);
        }

        errors.AddRange(failedChecks);

        return new PdfAValidationResult
        {
            Success = true,
            IsPdfA = !string.IsNullOrWhiteSpace(profileName),
            IsCompliant = isCompliant,
            ProfileName = profileName,
            RawReport = rawReport,
            Errors = errors
        };
    }

    private static bool IsFailedCheckNode(XElement element)
    {
        if (!element.Name.LocalName.Equals("rule", StringComparison.OrdinalIgnoreCase)
            && !element.Name.LocalName.Equals("test", StringComparison.OrdinalIgnoreCase)
            && !element.Name.LocalName.Equals("check", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsFailureValue(ReadAttribute(element, "status"))
            || IsFailureValue(ReadAttribute(element, "result"))
            || IsFailureValue(ReadElementValue(element, "status"))
            || IsFailureValue(ReadElementValue(element, "result"))
            || IsPassedFalse(ReadAttribute(element, "passed"))
            || IsPassedFalse(ReadElementValue(element, "passed"));
    }

    private static string? DescribeFailure(XElement element)
    {
        return ReadAttribute(element, "specification")
            ?? ReadAttribute(element, "clause")
            ?? ReadAttribute(element, "testNumber")
            ?? ReadElementValue(element, "message")
            ?? ReadElementValue(element, "description")
            ?? CompactText(element.Value);
    }

    private static bool IsFailureValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || value.Equals("fail", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPassedFalse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ReadBool(XElement? element, string name)
    {
        var value = ReadAttribute(element, name) ?? ReadElementValue(element, name);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? ReadAttribute(XElement? element, string name)
    {
        return element?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    }

    private static string? ReadElementValue(XElement? element, string name)
    {
        return element?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
    }

    private static string? CompactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Join(' ', value.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
    }
}
