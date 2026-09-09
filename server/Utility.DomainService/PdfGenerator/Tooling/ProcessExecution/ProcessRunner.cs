using System.Diagnostics;
using Utility.DomainService.PdfGenerator.Tooling.Interfaces;
using Utility.DomainService.PdfGenerator.Tooling.Models;

namespace Utility.DomainService.PdfGenerator.Tooling.ProcessExecution;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly IExternalProcessConcurrencyLimiter _concurrencyLimiter;

    public ProcessRunner(IExternalProcessConcurrencyLimiter concurrencyLimiter)
    {
        _concurrencyLimiter = concurrencyLimiter;
    }

    public async Task<ProcessResult> RunAsync(ProcessCommand command, CancellationToken cancellationToken)
    {
        using var lease = await _concurrencyLimiter.AcquireAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, command.TimeoutSeconds)));

        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = command.WorkingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        PrependExecutableDirectoryToPath(startInfo, command.FileName);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            stopwatch.Stop();
            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = await stdoutTask,
                StandardError = await stderrTask,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            stopwatch.Stop();
            return new ProcessResult
            {
                ExitCode = -1,
                StandardError = "Process timed out.",
                Duration = stopwatch.Elapsed,
                TimedOut = true
            };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // The executable could not even be started - a missing binary, an invalid working
            // directory, a permission error. Every tool wrapper (QpdfPdfNormalizer,
            // VeraPdfAValidator, PdfBoxGeometryNormalizer/PdfFlattener/StandardPdfConverter,
            // GhostscriptPdfAConverter) already treats a non-zero-exit ProcessResult as an
            // ordinary, graceful failure - reporting this the same way means a misconfigured or
            // missing tool surfaces through those existing fallback paths instead of throwing past
            // all of them and erasing whatever the pipeline had already legitimately determined
            // about the file (verified live: a missing Ghostscript binary during PDF/A repair used
            // to make the whole verdict falsely report the file as completely unreadable).
            stopwatch.Stop();
            return new ProcessResult
            {
                ExitCode = -1,
                StandardError = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private static void PrependExecutableDirectoryToPath(ProcessStartInfo startInfo, string fileName)
    {
        if (!Path.IsPathRooted(fileName))
        {
            return;
        }

        var executableDirectory = Path.GetDirectoryName(fileName);
        if (string.IsNullOrWhiteSpace(executableDirectory))
        {
            return;
        }

        const string pathKey = "PATH";
        var currentPath = startInfo.Environment.TryGetValue(pathKey, out var configuredPath)
            ? configuredPath
            : Environment.GetEnvironmentVariable(pathKey);

        startInfo.Environment[pathKey] = string.IsNullOrWhiteSpace(currentPath)
            ? executableDirectory
            : $"{executableDirectory}{Path.PathSeparator}{currentPath}";
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
