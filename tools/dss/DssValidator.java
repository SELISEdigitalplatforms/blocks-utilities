import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import eu.europa.esig.dss.diagnostic.CertificateRevocationWrapper;
import eu.europa.esig.dss.diagnostic.CertificateWrapper;
import eu.europa.esig.dss.diagnostic.DiagnosticData;
import eu.europa.esig.dss.diagnostic.PDFRevisionWrapper;
import eu.europa.esig.dss.diagnostic.RelatedRevocationWrapper;
import eu.europa.esig.dss.diagnostic.SignatureWrapper;
import eu.europa.esig.dss.enumerations.RevocationOrigin;
import eu.europa.esig.dss.enumerations.SignatureLevel;
import eu.europa.esig.dss.model.FileDocument;
import eu.europa.esig.dss.model.x509.CertificateToken;
import eu.europa.esig.dss.pades.validation.PDFDocumentValidator;
import eu.europa.esig.dss.service.http.commons.CommonsDataLoader;
import eu.europa.esig.dss.service.http.commons.FileCacheDataLoader;
import eu.europa.esig.dss.simplereport.SimpleReport;
import eu.europa.esig.dss.spi.DSSUtils;
import eu.europa.esig.dss.spi.client.http.IgnoreDataLoader;
import eu.europa.esig.dss.spi.tsl.TrustedListsCertificateSource;
import eu.europa.esig.dss.spi.validation.CommonCertificateVerifier;
import eu.europa.esig.dss.spi.x509.CommonTrustedCertificateSource;
import eu.europa.esig.dss.spi.x509.CommonCertificateSource;
import eu.europa.esig.dss.tsl.function.OfficialJournalSchemeInformationURI;
import eu.europa.esig.dss.tsl.job.TLValidationJob;
import eu.europa.esig.dss.tsl.source.LOTLSource;
import eu.europa.esig.dss.validation.reports.Reports;
import java.io.BufferedReader;
import java.io.ByteArrayInputStream;
import java.io.File;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.util.ArrayList;
import java.util.Date;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Stream;

/**
 * Validates the signatures of PDF files with EU DSS, for the Worker's signature-validation consumer.
 *
 * <p>Runs as one long-lived process per worker (docs/pdf-signature-validation/README.md, Runtime):
 * loading the trusted lists is the slow part, so it happens once and is refreshed in the
 * background, and each file is then a request on stdin answered on stdout, one JSON object per
 * line. stdout carries nothing else; DSS's own logging goes to stderr through slf4j-simple.
 *
 * <p>The downloaded lists are kept in {@code --cache-dir}. Every start loads them from there first
 * ({@code offlineRefresh}), so a process restarted after a timeout or crash is ready in seconds;
 * only a new container with an empty directory has to wait for the download.
 *
 * <p>Revocation data is never fetched: the verifier has no OCSP, CRL or AIA source, so a
 * signature reaches LT only on the data the document itself carries. That is the point of the
 * check, not an optimisation.
 */
public final class DssValidator {
    private static final String DEFAULT_LOTL_URL = "https://ec.europa.eu/tools/lotl/eu-lotl.xml";
    private static final String DEFAULT_OJ_URL = "https://eur-lex.europa.eu/eli/C/2026/1944/oj";

    private static final ObjectMapper JSON = new ObjectMapper();
    private static final PrintStream OUT = new PrintStream(System.out, true, StandardCharsets.UTF_8);

    private final TrustedListsCertificateSource trustedLists = new TrustedListsCertificateSource();
    private final CommonTrustedCertificateSource extraAnchors = new CommonTrustedCertificateSource();
    private final TLValidationJob job = new TLValidationJob();
    private final Path cacheDir;
    private volatile Instant trustedListsLoadedAt;

    private DssValidator(Map<String, String> options) throws Exception {
        cacheDir = Path.of(required(options, "--cache-dir"));
        Files.createDirectories(cacheDir);

        LOTLSource lotl = new LOTLSource();
        lotl.setUrl(options.getOrDefault("--lotl-url", DEFAULT_LOTL_URL));
        // The LOTL is only accepted when signed by a key the Commission announced in the Official
        // Journal; pivot support follows the announced key changes from there.
        lotl.setCertificateSource(certificates(required(options, "--lotl-signers")));
        lotl.setSigningCertificatesAnnouncementPredicate(
                new OfficialJournalSchemeInformationURI(options.getOrDefault("--oj-url", DEFAULT_OJ_URL)));
        lotl.setPivotSupport(true);

        job.setListOfTrustedListSources(lotl);
        job.setTrustedListCertificateSource(trustedLists);
        job.setOnlineDataLoader(fileLoader(new CommonsDataLoader()));
        job.setOfflineDataLoader(fileLoader(new IgnoreDataLoader()));

        String anchors = options.get("--anchors");
        if (anchors != null) {
            for (CertificateToken anchor : certificates(anchors).getCertificates()) {
                extraAnchors.addCertificate(anchor);
            }
        }
    }

    public static void main(String[] args) throws Exception {
        if (args.length == 1 && "--version".equals(args[0])) {
            // DSS's manifests carry no Implementation-Version; the Maven metadata in its jar does.
            java.util.Properties pom = new java.util.Properties();
            try (InputStream stream = PDFDocumentValidator.class.getResourceAsStream(
                    "/META-INF/maven/eu.europa.ec.joinup.sd-dss/dss-pades/pom.properties")) {
                pom.load(stream);
            }
            OUT.println("EU DSS " + pom.getProperty("version"));
            return;
        }

        Map<String, String> options = parse(args);
        DssValidator validator = new DssValidator(options);
        validator.loadFromCache();

        String once = options.get("--once");
        if (once != null) {
            // Manual checks and support: wait for the lists if the cache was empty, answer one file.
            if (!validator.isReady()) {
                validator.refreshOnline();
            }
            long started = System.nanoTime();
            OUT.println(validator.validate("once", once));
            System.err.println("Validated in " + (System.nanoTime() - started) / 1_000_000 + " ms");
            // DSS's loader threads are not daemons and idle for a minute before the JVM may exit.
            System.exit(0);
        }

        long refreshHours = Long.parseLong(options.getOrDefault("--refresh-hours", "24"));
        Executors.newSingleThreadScheduledExecutor(runnable -> {
            Thread thread = new Thread(runnable, "tl-refresh");
            thread.setDaemon(true);
            return thread;
        }).scheduleWithFixedDelay(validator::refreshOnline, 0, refreshHours, TimeUnit.HOURS);

        validator.serve(new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8)));
        // stdin closed: the Worker is stopping this process. Don't wait on DSS's loader threads.
        System.exit(0);
    }

    private void serve(BufferedReader requests) throws Exception {
        String line;
        while ((line = requests.readLine()) != null) {
            if (line.isBlank()) {
                continue;
            }
            JsonNode request;
            try {
                request = JSON.readTree(line);
            } catch (Exception exception) {
                OUT.println(failure(null, "invalid_request", "The request line is not JSON."));
                continue;
            }
            String id = request.path("id").asText(null);
            switch (request.path("op").asText("")) {
                case "status" -> OUT.println(status(id));
                case "validate" -> OUT.println(validate(id, request.path("path").asText("")));
                default -> OUT.println(failure(id, "invalid_request", "op must be status or validate."));
            }
        }
    }

    private ObjectNode status(String id) {
        ObjectNode response = JSON.createObjectNode().put("id", id).put("ok", true);
        response.put("ready", isReady());
        response.put("trustedListsLoadedAt", text(trustedListsLoadedAt));
        response.put("trustedCertificateCount", trustedLists.getNumberOfCertificates());
        return response;
    }

    private ObjectNode validate(String id, String path) {
        if (!isReady()) {
            // Not a verdict: the consumer re-queues the job until the lists load (spec, Edge Cases).
            return failure(id, "trust_lists_unavailable", "No trusted list has loaded yet.");
        }
        File file = new File(path);
        if (!file.isFile()) {
            return failure(id, "input_file_not_found", "No file at the given path.");
        }
        try {
            if (!startsLikePdf(file)) {
                return failure(id, "input_not_pdf", "The file is not a PDF.");
            }
            return verdict(id, validateFile(file));
        } catch (Exception exception) {
            if (isPasswordProblem(exception)) {
                return failure(id, "input_password_protected", "The PDF needs a password to open.");
            }
            return failure(id, "validator_error", exception.getClass().getSimpleName() + ": " + exception.getMessage());
        }
    }

    private Reports validateFile(File file) {
        CommonCertificateVerifier verifier = new CommonCertificateVerifier();
        verifier.setTrustedCertSources(trustedLists, extraAnchors);
        verifier.setAIASource(null);
        verifier.setOcspSource(null);
        verifier.setCrlSource(null);

        PDFDocumentValidator validator = new PDFDocumentValidator(new FileDocument(file));
        validator.setCertificateVerifier(verifier);
        return validator.validateDocument();
    }

    private ObjectNode verdict(String id, Reports reports) {
        SimpleReport simple = reports.getSimpleReport();
        DiagnosticData diagnostic = reports.getDiagnosticData();

        ArrayNode signatures = JSON.createArrayNode();
        boolean allPassed = true;
        SignatureLevel lowest = null;
        List<String> ids = simple.getSignatureIdList();
        for (String signatureId : ids) {
            SignatureWrapper signature = diagnostic.getSignatureById(signatureId);
            PDFRevisionWrapper revision = signature.getPDFRevision();
            CertificateWrapper signer = signature.getSigningCertificate();
            SignatureLevel level = simple.getSignatureFormat(signatureId);
            boolean passed = simple.getIndication(signatureId) == eu.europa.esig.dss.enumerations.Indication.TOTAL_PASSED;

            allPassed &= passed;
            if (lowest == null || rank(level) < rank(lowest)) {
                lowest = level;
            }

            ObjectNode node = signatures.addObject();
            node.put("fieldName", revision == null ? null : revision.getFirstFieldName());
            node.put("indication", text(simple.getIndication(signatureId)));
            node.put("subIndication", text(simple.getSubIndication(signatureId)));
            node.put("signatureLevel", text(level));
            node.put("signerSubject", signer == null ? null : signer.getCertificateDN());
            node.put("claimedSigningTime", text(signature.getClaimedSigningTime()));
            node.put("bestSignatureTime", text(simple.getBestSignatureTime(signatureId)));
            node.put("revocationOrigin", revocationOrigin(signature, signer));
            node.put("coversWholeDocument", revision != null && revision.isSignatureByteRangeValid());
        }

        ObjectNode verdict = JSON.createObjectNode();
        verdict.put("signatureCount", ids.size());
        verdict.put("lowestLevel", text(lowest));
        verdict.put("allPassed", !ids.isEmpty() && allPassed);
        verdict.put("validationTime", text(simple.getValidationTime()));
        verdict.put("trustedListsLoadedAt", text(trustedListsLoadedAt));
        verdict.set("signatures", signatures);

        ObjectNode response = JSON.createObjectNode().put("id", id).put("ok", true);
        response.set("verdict", verdict);
        return response;
    }

    /**
     * Where the signer's revocation data came from, as the spec's {@code RevocationOrigin}:
     * {@code DssDictionary}, {@code Cms} or {@code None}.
     *
     * <p>A revocation's own {@code getOrigin()} only says "from the document" ({@code INPUT_DOCUMENT})
     * versus online or cached. Where in the document it sat is recorded per signature, on its found
     * revocations, so the signer certificate's revocations are matched to those by id.
     */
    private static String revocationOrigin(SignatureWrapper signature, CertificateWrapper signer) {
        if (signer == null) {
            return "None";
        }
        Set<String> signerRevocations = new HashSet<>();
        for (CertificateRevocationWrapper revocation : signer.getCertificateRevocationData()) {
            signerRevocations.add(revocation.getId());
        }
        // DssDictionary wins when the same data is in both places: the question this field answers
        // (spec 009) is whether the document's /DSS is what validation relied on, and a copy also
        // carried in the CMS does not make the /DSS copy unused.
        boolean inCms = false;
        for (RelatedRevocationWrapper found : signature.foundRevocations().getRelatedRevocationData()) {
            if (!signerRevocations.contains(found.getId())) {
                continue;
            }
            for (RevocationOrigin origin : found.getOrigins()) {
                switch (origin) {
                    case DSS_DICTIONARY, VRI_DICTIONARY -> {
                        return "DssDictionary";
                    }
                    case CMS_SIGNED_DATA, ADBE_REVOCATION_INFO_ARCHIVAL, REVOCATION_VALUES -> inCms = true;
                    default -> {
                        // Timestamp or evidence-record data does not cover the signer; online
                        // sources (EXTERNAL, CACHED) are not configured.
                    }
                }
            }
        }
        return inCms ? "Cms" : "None";
    }

    /** B < T < LT < LTA; anything that is not a PAdES baseline level ranks below all of them. */
    private static int rank(SignatureLevel level) {
        if (level == null) {
            return 0;
        }
        return switch (level) {
            case PAdES_BASELINE_B -> 1;
            case PAdES_BASELINE_T -> 2;
            case PAdES_BASELINE_LT -> 3;
            case PAdES_BASELINE_LTA -> 4;
            default -> 0;
        };
    }

    private void loadFromCache() {
        long started = System.nanoTime();
        job.offlineRefresh();
        markLoaded(newestCacheFile());
        System.err.println("Trusted lists from cache: " + trustedLists.getNumberOfCertificates()
                + " certificates in " + (System.nanoTime() - started) / 1_000_000 + " ms");
    }

    private void refreshOnline() {
        try {
            job.onlineRefresh();
            markLoaded(Instant.now());
        } catch (Exception exception) {
            // Keep the lists already loaded (spec, Edge Cases); the age in /status tells operators.
            System.err.println("Trusted-list refresh failed: " + exception);
        }
    }

    private void markLoaded(Instant when) {
        if (trustedLists.getNumberOfCertificates() > 0 && when != null) {
            trustedListsLoadedAt = when;
        }
    }

    private boolean isReady() {
        return trustedListsLoadedAt != null;
    }

    /** An offline load is only as fresh as the files it read, not the moment it read them. */
    private Instant newestCacheFile() {
        try (Stream<Path> files = Files.list(cacheDir)) {
            return files.map(path -> path.toFile().lastModified())
                    .max(Long::compare)
                    .map(Instant::ofEpochMilli)
                    .orElse(null);
        } catch (Exception exception) {
            return null;
        }
    }

    private FileCacheDataLoader fileLoader(eu.europa.esig.dss.spi.client.http.DataLoader transport) {
        FileCacheDataLoader loader = new FileCacheDataLoader(transport);
        loader.setFileCacheDirectory(cacheDir.toFile());
        loader.setCacheExpirationTime(-1); // never expire by age; the scheduled refresh replaces them
        return loader;
    }

    private static CommonCertificateSource certificates(String pemPath) throws Exception {
        CommonCertificateSource source = new CommonCertificateSource();
        Matcher pem = Pattern.compile("-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----", Pattern.DOTALL)
                .matcher(Files.readString(Path.of(pemPath), StandardCharsets.US_ASCII));
        List<CertificateToken> found = new ArrayList<>();
        while (pem.find()) {
            found.add(DSSUtils.loadCertificate(new ByteArrayInputStream(pem.group().getBytes(StandardCharsets.US_ASCII))));
        }
        if (found.isEmpty()) {
            throw new IllegalArgumentException("No certificate in " + pemPath);
        }
        found.forEach(source::addCertificate);
        return source;
    }

    private static boolean startsLikePdf(File file) throws Exception {
        byte[] head = new byte[1024];
        int read;
        try (InputStream stream = Files.newInputStream(file.toPath())) {
            read = stream.readNBytes(head, 0, head.length);
        }
        return new String(head, 0, read, StandardCharsets.ISO_8859_1).contains("%PDF-");
    }

    private static boolean isPasswordProblem(Throwable exception) {
        for (Throwable cause = exception; cause != null; cause = cause.getCause()) {
            if (cause.getClass().getSimpleName().contains("Password")) {
                return true;
            }
        }
        return false;
    }

    private static ObjectNode failure(String id, String errorCode, String errorMessage) {
        return JSON.createObjectNode().put("id", id).put("ok", false)
                .put("errorCode", errorCode).put("errorMessage", errorMessage);
    }

    private static String text(Object value) {
        if (value == null) {
            return null;
        }
        if (value instanceof Date date) {
            return date.toInstant().toString();
        }
        return value.toString();
    }

    private static Map<String, String> parse(String[] args) {
        Map<String, String> options = new HashMap<>();
        for (int i = 0; i + 1 < args.length; i += 2) {
            options.put(args[i], args[i + 1]);
        }
        return options;
    }

    private static String required(Map<String, String> options, String name) {
        String value = options.get(name);
        if (value == null || value.isBlank()) {
            System.err.println("USAGE: DssValidator --cache-dir <dir> --lotl-signers <pem> [--anchors <pem>]"
                    + " [--lotl-url <url>] [--oj-url <url>] [--refresh-hours <h>] [--once <pdf>]");
            System.exit(1);
        }
        return value;
    }
}
