using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfSignatureValidation.Validator
{
    /// <summary>
    /// The real <see cref="IValidatorChannel"/>: a child JVM running <c>DssValidator</c>, spoken to
    /// over its stdin and stdout.
    /// </summary>
    /// <remarks>
    /// stderr is read continuously and forwarded to the log, never left alone. DSS writes a stream of
    /// warnings there, and a pipe nobody reads fills up and blocks the JVM mid-write - a hang that
    /// looks exactly like a slow file until the per-file timeout kills it.
    /// </remarks>
    public sealed class ValidatorProcessChannel : IValidatorChannel
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly Process _process;
        private readonly ILogger _logger;
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ValidatorProcessChannel(Process process, ILogger logger)
        {
            _process = process;
            _logger = logger;
        }

        public Task Exited => _exited.Task;

        internal static ValidatorProcessChannel Start(PdfSignatureValidatorOptions options, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(options);

            var startInfo = new ProcessStartInfo
            {
                FileName = options.JavaPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Utf8NoBom,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in Arguments(options))
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var channel = new ValidatorProcessChannel(process, logger);

            process.Exited += (_, _) => channel._exited.TrySetResult();
            process.ErrorDataReceived += (_, e) => channel.ForwardStderr(e.Data);

            try
            {
                process.Start();
                process.BeginErrorReadLine();
            }
            catch
            {
                process.Dispose();
                throw;
            }

            // An exit that raced the subscription above would otherwise never complete Exited.
            if (process.HasExited)
            {
                channel._exited.TrySetResult();
            }

            return channel;
        }

        /// <summary>
        /// The JVM's command line. The font logger is quietened because PDFBox warns once per Type 1
        /// font about a substituted fallback, dozens of lines per file that say nothing about the
        /// validation; everything else DSS warns about is kept.
        /// </summary>
        public static IReadOnlyList<string> Arguments(PdfSignatureValidatorOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> arguments =
            [
                $"-Xmx{options.HeapMegabytes}m",
                "-Djava.awt.headless=true",
                "-Dorg.slf4j.simpleLogger.defaultLogLevel=warn",
                "-Dorg.slf4j.simpleLogger.log.org.apache.pdfbox.pdmodel.font=error",
                "-cp",
                options.ClassPath,
                "DssValidator",
                "--cache-dir",
                options.TrustedListCacheDirectory,
                "--lotl-signers",
                options.LotlSignersPath
            ];

            if (!string.IsNullOrWhiteSpace(options.ExtraAnchorsPath))
            {
                arguments.Add("--anchors");
                arguments.Add(options.ExtraAnchorsPath);
            }

            arguments.Add("--refresh-hours");
            arguments.Add(options.TrustedListRefreshHours.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return arguments;
        }

        public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                // Already gone between the check and the kill, or not ours to kill. Either way this
                // is called from cleanup paths that must finish, so it logs and moves on.
                _logger.LogWarning(ex, "DssValidator: Could not kill the validator process");
            }
        }

        public void Dispose()
        {
            Kill();
            _process.Dispose();
        }

        private void ForwardStderr(string? line)
        {
            // Runs on a thread-pool thread as an event handler, where an exception is not caught by
            // anything and ends the whole process. Logging a line must never be able to do that.
            try
            {
                Forward(line);
            }
            catch (Exception)
            {
                // Nothing useful left to do: the log itself is what failed.
            }
        }

        private void Forward(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            // A stack trace or an Error is the validator telling us something broke; the rest is
            // DSS's routine warnings about individual trusted lists and certificates.
            var level = line.Contains("Exception", StringComparison.Ordinal)
                || line.Contains("Error", StringComparison.Ordinal)
                ? LogLevel.Warning
                : LogLevel.Information;

            _logger.Log(level, "DssValidator: {Line}", LogSanitizer.Scrub(line));
        }
    }

    public sealed class ValidatorProcessChannelFactory : IValidatorChannelFactory
    {
        private readonly ILogger<ValidatorProcessChannel> _logger;

        public ValidatorProcessChannelFactory(ILogger<ValidatorProcessChannel> logger) =>
            _logger = logger;

        public IValidatorChannel Start(PdfSignatureValidatorOptions options) =>
            ValidatorProcessChannel.Start(options, _logger);
    }
}
