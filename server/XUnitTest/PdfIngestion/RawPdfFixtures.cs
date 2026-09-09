using System.Text;

namespace XUnitTest.PdfIngestion;

/// <summary>
/// Builds minimal, hand-crafted PDF byte sequences for cases a normal PDF writer will not produce
/// on its own (an indirect /MediaBox, a bare signature dictionary) - every offset in the resulting
/// xref table is computed from the actual bytes written, so the fixture is a real parseable PDF,
/// not a recorded sample.
/// </summary>
internal static class RawPdfFixtures
{
    public static byte[] BuildPdf(int rootObjectNumber, params string[] objectBodies)
    {
        var header = "%PDF-1.7\n%âãÏÓ\n";
        var builder = new StringBuilder(header);
        var offsets = new List<int>();

        for (var i = 0; i < objectBodies.Length; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(builder.ToString()));
            builder.Append(i + 1).Append(" 0 obj\n").Append(objectBodies[i]).Append("\nendobj\n");
        }

        var xrefOffset = Encoding.Latin1.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objectBodies.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objectBodies.Length + 1)
            .Append(" /Root ").Append(rootObjectNumber).Append(" 0 R >>\nstartxref\n")
            .Append(xrefOffset).Append("\n%%EOF");

        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    /// <summary>Wraps XMP content as a /Metadata stream object body, with /Length computed for real.</summary>
    public static string MetadataStream(string xmpPacket)
    {
        var length = Encoding.Latin1.GetByteCount(xmpPacket);
        return $"<< /Type /Metadata /Subtype /XML /Length {length} >>\nstream\n{xmpPacket}\nendstream";
    }

    public static string PdfAXmpPacket(string part, string? conformance)
    {
        var conformanceElement = conformance is null ? string.Empty : $"   <pdfaid:conformance>{conformance}</pdfaid:conformance>\n";
        return "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
               "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
               " <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
               "  <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\">\n" +
               $"   <pdfaid:part>{part}</pdfaid:part>\n" +
               conformanceElement +
               "  </rdf:Description>\n" +
               " </rdf:RDF>\n" +
               "</x:xmpmeta>\n" +
               "<?xpacket end=\"w\"?>";
    }
}
