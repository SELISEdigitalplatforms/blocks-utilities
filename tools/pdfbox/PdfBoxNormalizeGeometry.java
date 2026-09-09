import java.io.File;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import org.apache.pdfbox.Loader;
import org.apache.pdfbox.cos.COSArray;
import org.apache.pdfbox.cos.COSBase;
import org.apache.pdfbox.cos.COSDictionary;
import org.apache.pdfbox.cos.COSFloat;
import org.apache.pdfbox.cos.COSName;
import org.apache.pdfbox.cos.COSObject;
import org.apache.pdfbox.pdfwriter.compress.CompressParameters;
import org.apache.pdfbox.pdmodel.PDDocument;
import org.apache.pdfbox.pdmodel.PDPage;
import org.apache.pdfbox.pdmodel.PDPageTree;
import org.apache.pdfbox.pdmodel.common.PDRectangle;
import org.apache.pdfbox.pdmodel.interactive.digitalsignature.PDSignature;

/**
 * Rewrites every page's box entries as DIRECT number arrays.
 *
 * <p>PDF 32000-1 7.3.6 allows any array element to be an indirect reference, so a producer may
 * legally emit {@code /MediaBox [715 0 R 715 0 R 596.52 843]}. PdfSharpCore's value accessors are
 * reference-blind and throw {@code InvalidCastException: GetReal: Object is not a number} on such a
 * file. PDFBox resolves both indirect references and page-tree inheritance, so reading each box
 * through the PD model and writing it straight back materialises direct values that PdfSharpCore
 * can read. Nothing else in the document is touched.
 *
 * <p>Emits a one-line JSON report on stdout prefixed with {@link #REPORT_MARKER} so the caller can
 * verify that page geometry did not shift.
 */
public final class PdfBoxNormalizeGeometry {

    static final String REPORT_MARKER = "##PDFBOX_GEOMETRY##";

    private static final COSName[] NON_INHERITABLE_BOXES = {
        COSName.BLEED_BOX, COSName.TRIM_BOX, COSName.ART_BOX
    };

    private PdfBoxNormalizeGeometry() {
    }

    public static void main(String[] args) {
        if (args.length == 1 && "--version".equals(args[0])) {
            System.out.println("Apache PDFBox " + org.apache.pdfbox.util.Version.getVersion());
            return;
        }

        if (args.length != 2) {
            System.err.println("USAGE: PdfBoxNormalizeGeometry <input.pdf> <output.pdf>");
            System.exit(1);
        }

        try (PDDocument document = Loader.loadPDF(new File(args[0]))) {
            List<PageReport> pages = new ArrayList<>();
            boolean changed = false;

            for (PDPage page : document.getPages()) {
                PageReport report = normalizePage(page, pages.size());
                pages.add(report);
                changed |= !report.rewrittenBoxes.isEmpty();
            }

            // Materialising the boxes on the page is not enough on its own: PdfSharpCore pushes
            // inheritable attributes down from the /Pages nodes, so an indirect box left on an
            // ancestor overwrites the direct one we just wrote and the file still fails to read.
            changed |= normalizePageTreeNodes(document);

            // A full re-save rewrites every object, so byte offsets move and any /ByteRange in an
            // existing signature stops matching. Refuse rather than silently invalidate: the caller
            // keeps such documents on its own (iText) path.
            List<PDSignature> signatures = document.getSignatureDictionaries();
            boolean signed = signatures != null && !signatures.isEmpty();
            if (signed) {
                System.out.println(buildReport(pages, signed, false));
                System.err.println("SIGNED_DOCUMENT");
                System.exit(3);
            }

            // NO_COMPRESSION keeps the page dictionaries out of /ObjStm, matching the deliberate
            // --object-streams=disable on the sibling qpdf path and leaving the result verifiable
            // by inspection.
            document.save(new File(args[1]), CompressParameters.NO_COMPRESSION);

            System.out.println(buildReport(pages, signed, changed));
        } catch (Exception exception) {
            System.err.println(exception.getClass().getSimpleName() + ": " + exception.getMessage());
            System.exit(1);
        }
    }

    /**
     * Rewrites inheritable box entries on the /Pages nodes as direct values. Walks up each page's
     * /Parent chain rather than down from the catalog, so only nodes that actually govern a page
     * are touched.
     */
    private static boolean normalizePageTreeNodes(PDDocument document) {
        List<COSDictionary> visited = new ArrayList<>();
        boolean changed = false;

        for (PDPage page : document.getPages()) {
            COSBase parent = page.getCOSObject().getDictionaryObject(COSName.PARENT);

            while (parent instanceof COSDictionary) {
                COSDictionary node = (COSDictionary) parent;

                boolean seen = false;
                for (COSDictionary candidate : visited) {
                    if (candidate == node) {
                        seen = true;
                        break;
                    }
                }
                if (seen) {
                    break;
                }
                visited.add(node);

                for (COSName key : new COSName[] {COSName.MEDIA_BOX, COSName.CROP_BOX}) {
                    if (node.getItem(key) == null || !isIndirect(node, key)) {
                        continue;
                    }

                    COSBase resolved = node.getDictionaryObject(key);
                    if (resolved instanceof COSArray) {
                        node.setItem(key, toDirectArray(new PDRectangle((COSArray) resolved)));
                        changed = true;
                    }
                }

                if (node.getItem(COSName.ROTATE) != null && isIndirect(node, COSName.ROTATE)) {
                    node.setInt(COSName.ROTATE, node.getInt(COSName.ROTATE, 0));
                    changed = true;
                }

                parent = node.getDictionaryObject(COSName.PARENT);
            }
        }

        return changed;
    }

    private static PageReport normalizePage(PDPage page, int index) {
        COSDictionary dict = page.getCOSObject();
        PageReport report = new PageReport();
        report.index = index;

        // Read every value through the PD model BEFORE overwriting the dictionary: PDPage caches
        // mediaBox/cropBox in fields, and the getters are what resolve references and inheritance.
        if (isIndirect(dict, COSName.MEDIA_BOX)) {
            report.indirectBoxes.add("MediaBox");
        }
        PDRectangle media = page.getMediaBox();
        if (PDPageTree.getInheritableAttribute(dict, COSName.MEDIA_BOX) == null) {
            report.warnings.add("PAGE_" + index + "_MEDIABOX_MISSING_DEFAULTED");
        }

        boolean hasCropBox = PDPageTree.getInheritableAttribute(dict, COSName.CROP_BOX) != null;
        if (isIndirect(dict, COSName.CROP_BOX)) {
            report.indirectBoxes.add("CropBox");
        }
        PDRectangle crop = hasCropBox ? page.getCropBox() : media;

        if (isIndirect(dict, COSName.ROTATE)) {
            report.indirectBoxes.add("Rotate");
        }
        int rotate = page.getRotation();

        // MediaBox is required; always materialise it at page level so nothing downstream depends
        // on inheritance resolution either.
        dict.setItem(COSName.MEDIA_BOX, toDirectArray(media));
        report.rewrittenBoxes.add("MediaBox");

        if (hasCropBox) {
            dict.setItem(COSName.CROP_BOX, toDirectArray(crop));
            report.rewrittenBoxes.add("CropBox");
        }

        // Bleed/Trim/Art are not inheritable and default to CropBox, so only rewrite them when the
        // key is physically present on this page.
        for (COSName key : NON_INHERITABLE_BOXES) {
            if (dict.getItem(key) == null) {
                continue;
            }

            if (isIndirect(dict, key)) {
                report.indirectBoxes.add(key.getName());
            }

            COSBase resolved = dict.getDictionaryObject(key);
            if (resolved instanceof COSArray) {
                dict.setItem(key, toDirectArray(new PDRectangle((COSArray) resolved)));
                report.rewrittenBoxes.add(key.getName());
            }
        }

        page.setRotation(rotate);

        report.rotate = rotate;
        report.mediaBox = media;
        report.cropBox = crop;
        return report;
    }

    /**
     * True when the stored value is itself an indirect reference, or an array holding one. Uses
     * {@code getItem} rather than {@code getDictionaryObject} precisely because the latter
     * dereferences and would hide what we are looking for.
     */
    private static boolean isIndirect(COSDictionary pageDictionary, COSName key) {
        COSBase raw = pageDictionary.getItem(key);
        if (raw == null) {
            return false;
        }

        if (raw instanceof COSObject) {
            return true;
        }

        if (raw instanceof COSArray) {
            COSArray array = (COSArray) raw;
            for (int i = 0; i < array.size(); i++) {
                if (array.get(i) instanceof COSObject) {
                    return true;
                }
            }
        }

        return false;
    }

    private static COSArray toDirectArray(PDRectangle rectangle) {
        COSArray array = new COSArray();
        array.add(new COSFloat(rectangle.getLowerLeftX()));
        array.add(new COSFloat(rectangle.getLowerLeftY()));
        array.add(new COSFloat(rectangle.getUpperRightX()));
        array.add(new COSFloat(rectangle.getUpperRightY()));
        array.setDirect(true);
        return array;
    }

    private static String buildReport(List<PageReport> pages, boolean signed, boolean changed) {
        StringBuilder builder = new StringBuilder(REPORT_MARKER);
        builder.append("{\"engine\":\"pdfbox\",\"version\":\"")
            .append(escape(org.apache.pdfbox.util.Version.getVersion()))
            .append("\",\"pageCount\":").append(pages.size())
            .append(",\"signed\":").append(signed)
            .append(",\"changed\":").append(changed)
            .append(",\"pages\":[");

        List<String> warnings = new ArrayList<>();
        for (int i = 0; i < pages.size(); i++) {
            if (i > 0) {
                builder.append(',');
            }
            pages.get(i).appendTo(builder);
            warnings.addAll(pages.get(i).warnings);
        }

        builder.append("],\"warnings\":[");
        for (int i = 0; i < warnings.size(); i++) {
            if (i > 0) {
                builder.append(',');
            }
            builder.append('"').append(escape(warnings.get(i))).append('"');
        }
        builder.append("]}");
        return builder.toString();
    }

    /** Always Locale.ROOT: a comma-decimal container would otherwise emit 596,52 and break JSON. */
    private static String number(float value) {
        return String.format(Locale.ROOT, "%.4f", value);
    }

    private static String escape(String value) {
        if (value == null) {
            return "";
        }
        return value.replace("\\", "\\\\").replace("\"", "\\\"");
    }

    private static final class PageReport {
        private int index;
        private int rotate;
        private PDRectangle mediaBox;
        private PDRectangle cropBox;
        private final List<String> rewrittenBoxes = new ArrayList<>();
        private final List<String> indirectBoxes = new ArrayList<>();
        private final List<String> warnings = new ArrayList<>();

        private void appendTo(StringBuilder builder) {
            builder.append("{\"index\":").append(index)
                .append(",\"rotate\":").append(rotate)
                .append(",\"mediaBox\":");
            appendRectangle(builder, mediaBox);
            builder.append(",\"cropBox\":");
            appendRectangle(builder, cropBox);

            // Effective CropBox dimensions: that is what a viewer shows, and therefore what the
            // caller places stamps against.
            builder.append(",\"width\":").append(number(cropBox.getWidth()))
                .append(",\"height\":").append(number(cropBox.getHeight()))
                .append(",\"rewrittenBoxes\":");
            appendStrings(builder, rewrittenBoxes);
            builder.append(",\"indirectBoxes\":");
            appendStrings(builder, indirectBoxes);
            builder.append('}');
        }

        private static void appendRectangle(StringBuilder builder, PDRectangle rectangle) {
            builder.append('[').append(number(rectangle.getLowerLeftX()))
                .append(',').append(number(rectangle.getLowerLeftY()))
                .append(',').append(number(rectangle.getUpperRightX()))
                .append(',').append(number(rectangle.getUpperRightY()))
                .append(']');
        }

        private static void appendStrings(StringBuilder builder, List<String> values) {
            builder.append('[');
            for (int i = 0; i < values.size(); i++) {
                if (i > 0) {
                    builder.append(',');
                }
                builder.append('"').append(escape(values.get(i))).append('"');
            }
            builder.append(']');
        }
    }
}
