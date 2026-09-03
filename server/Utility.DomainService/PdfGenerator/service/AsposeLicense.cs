using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Applies the Aspose.Words licence to the process, exactly once.
    /// </summary>
    /// <remarks>
    /// Aspose.Words is licensed per process, not per document: an unlicensed instance renders an
    /// evaluation watermark onto every page and truncates long documents, so a converter that skips
    /// this produces output that looks plausible in a test and is unusable for a customer. The
    /// licence is read from configuration rather than committed, because it is a purchased
    /// credential — the repository's secret scan would flag it and Aspose's own terms mark it "not
    /// redistributable".
    ///
    /// Three sources are tried in order, so a container can supply the licence as an environment
    /// variable while a developer machine keeps a file on disk:
    /// <list type="number">
    ///   <item><c>Aspose:LicenseBase64</c> — the .lic file's bytes, base64 encoded.</item>
    ///   <item><c>Aspose:LicensePath</c> — an absolute or relative path to the .lic file.</item>
    ///   <item><c>Aspose.Words.lic</c> beside the running assembly.</item>
    /// </list>
    ///
    /// A missing licence is deliberately not fatal. The process still starts and every other feature
    /// keeps working; only Aspose-backed output is watermarked, and the warning below says so
    /// plainly rather than letting the watermark be discovered by a customer.
    /// </remarks>
    public static class AsposeLicense
    {
        private const string LicenseFileName = "Aspose.Words.lic";

        private static readonly object Gate = new();
        private static bool _attempted;

        /// <summary>
        /// True when a licence was found and accepted by Aspose. False means the process is in
        /// evaluation mode and Aspose output carries a watermark.
        /// </summary>
        public static bool IsLicensed { get; private set; }

        /// <summary>
        /// Applies the licence if it has not been applied already. Safe to call from every
        /// conversion; only the first call does any work.
        /// </summary>
        public static void EnsureApplied(IConfiguration configuration, ILogger logger)
        {
            if (_attempted)
            {
                return;
            }

            lock (Gate)
            {
                if (_attempted)
                {
                    return;
                }

                _attempted = true;
                IsLicensed = TryApply(configuration, logger);
            }
        }

        private static bool TryApply(IConfiguration configuration, ILogger logger)
        {
            try
            {
                var license = new Aspose.Words.License();

                var base64 = configuration["Aspose:LicenseBase64"];
                if (!string.IsNullOrWhiteSpace(base64))
                {
                    using var stream = new MemoryStream(Convert.FromBase64String(base64));
                    license.SetLicense(stream);
                    logger.LogInformation("AsposeLicense: Applied licence from Aspose:LicenseBase64");
                    return true;
                }

                var configuredPath = configuration["Aspose:LicensePath"];
                if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                {
                    license.SetLicense(configuredPath);
                    logger.LogInformation("AsposeLicense: Applied licence from {LicensePath}", configuredPath);
                    return true;
                }

                var localPath = Path.Combine(AppContext.BaseDirectory, LicenseFileName);
                if (File.Exists(localPath))
                {
                    license.SetLicense(localPath);
                    logger.LogInformation("AsposeLicense: Applied licence from {LicensePath}", localPath);
                    return true;
                }

                logger.LogWarning(
                    "AsposeLicense: No Aspose licence found (checked Aspose:LicenseBase64, Aspose:LicensePath and {LocalPath}). "
                        + "Aspose output will carry an evaluation watermark and long documents will be truncated.",
                    localPath);

                return false;
            }
            catch (Exception ex)
            {
                // The most common cause is a licence whose subscription expired before this
                // Aspose.Words release was published — Aspose rejects the pairing rather than
                // falling back — so the version is logged alongside the failure.
                logger.LogError(
                    ex,
                    "AsposeLicense: Failed to apply the Aspose licence. Output will carry an evaluation watermark. "
                        + "If this is a subscription-expiry error, the installed Aspose.Words version is newer than the licence allows.");

                return false;
            }
        }
    }
}
