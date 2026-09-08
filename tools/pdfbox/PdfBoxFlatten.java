import java.io.File;
import java.util.ArrayList;
import java.util.List;
import org.apache.pdfbox.Loader;
import org.apache.pdfbox.cos.COSName;
import org.apache.pdfbox.pdmodel.PDDocument;
import org.apache.pdfbox.pdmodel.interactive.form.PDAcroForm;
import org.apache.pdfbox.pdmodel.interactive.form.PDField;

public final class PdfBoxFlatten {
    private PdfBoxFlatten() {
    }

    public static void main(String[] args) {
        if (args.length == 1 && "--version".equals(args[0])) {
            System.out.println("Apache PDFBox " + org.apache.pdfbox.util.Version.getVersion());
            return;
        }

        boolean standardize = args.length == 3 && "--standardize".equals(args[0]);
        int inputIndex = standardize ? 1 : 0;
        int outputIndex = standardize ? 2 : 1;
        if ((!standardize && args.length != 2) || (standardize && args.length != 3)) {
            System.err.println("USAGE: PdfBoxFlatten [--standardize] <input.pdf> <output.pdf>");
            System.exit(1);
        }

        try (PDDocument document = Loader.loadPDF(new File(args[inputIndex]))) {
            PDAcroForm acroForm = document.getDocumentCatalog().getAcroForm();
            if (acroForm != null) {
                List<PDField> fields = new ArrayList<>();
                for (PDField field : acroForm.getFieldTree()) {
                    fields.add(field);
                }

                if (!fields.isEmpty()) {
                    acroForm.flatten(fields, true);
                }
            }

            if (standardize) {
                document.getDocumentCatalog().getCOSObject().removeItem(COSName.METADATA);
                document.getDocumentCatalog().getCOSObject().removeItem(COSName.OUTPUT_INTENTS);
            }

            document.save(args[outputIndex]);
        } catch (Exception exception) {
            System.err.println(exception.getClass().getSimpleName() + ": " + exception.getMessage());
            System.exit(1);
        }
    }
}
