using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.Qpdf;

public sealed class QpdfCommandFactory
{
    public ProcessCommand CreateNormalizeCommand(
        string inputPath,
        string outputPath,
        PdfNormalizeOptions normalizeOptions,
        PdfToolingOptions toolingOptions)
    {
        var qpdfArguments = CreateQpdfArguments(inputPath, outputPath, normalizeOptions);

        if (toolingOptions.QpdfExecutionMode == QpdfExecutionMode.Docker)
        {
            return CreateDockerNormalizeCommand(inputPath, outputPath, normalizeOptions, toolingOptions);
        }

        return new ProcessCommand
        {
            FileName = toolingOptions.QpdfPath,
            Arguments = qpdfArguments,
            WorkingDirectory = Path.GetDirectoryName(inputPath),
            TimeoutSeconds = normalizeOptions.TimeoutSeconds
        };
    }

    public ProcessCommand CreateVersionCommand(PdfToolingOptions toolingOptions)
    {
        if (toolingOptions.QpdfExecutionMode == QpdfExecutionMode.Docker)
        {
            return new ProcessCommand
            {
                FileName = toolingOptions.DockerPath,
                Arguments =
                [
                    "run",
                    "--rm",
                    "--network",
                    "none",
                    toolingOptions.QpdfDockerImage,
                    "qpdf",
                    "--version"
                ],
                TimeoutSeconds = 10
            };
        }

        return new ProcessCommand
        {
            FileName = toolingOptions.QpdfPath,
            Arguments = ["--version"],
            TimeoutSeconds = 10
        };
    }

    public ProcessCommand CreateCheckCommand(
        string outputPath,
        PdfToolingOptions toolingOptions,
        int timeoutSeconds)
    {
        if (toolingOptions.QpdfExecutionMode == QpdfExecutionMode.Docker)
        {
            return CreateDockerCheckCommand(outputPath, toolingOptions, timeoutSeconds);
        }

        return new ProcessCommand
        {
            FileName = toolingOptions.QpdfPath,
            Arguments = ["--check", outputPath],
            WorkingDirectory = Path.GetDirectoryName(outputPath),
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static ProcessCommand CreateDockerNormalizeCommand(
        string inputPath,
        string outputPath,
        PdfNormalizeOptions normalizeOptions,
        PdfToolingOptions toolingOptions)
    {
        var hostWorkspacePath = Path.GetDirectoryName(inputPath)
            ?? throw new InvalidOperationException("Input path must include a workspace directory.");

        var outputWorkspacePath = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Output path must include a workspace directory.");

        if (!Path.GetFullPath(hostWorkspacePath).Equals(Path.GetFullPath(outputWorkspacePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Docker qpdf mode requires input and output files to be in the same operation workspace.");
        }

        var containerWorkspacePath = NormalizeContainerPath(toolingOptions.DockerContainerWorkspacePath);
        var containerInputPath = $"{containerWorkspacePath}/{Path.GetFileName(inputPath)}";
        var containerOutputPath = $"{containerWorkspacePath}/{Path.GetFileName(outputPath)}";
        var qpdfArguments = CreateQpdfArguments(containerInputPath, containerOutputPath, normalizeOptions);

        return new ProcessCommand
        {
            FileName = toolingOptions.DockerPath,
            Arguments =
            [
                "run",
                "--rm",
                "--network",
                "none",
                "-v",
                $"{hostWorkspacePath}:{containerWorkspacePath}",
                toolingOptions.QpdfDockerImage,
                "qpdf",
                .. qpdfArguments
            ],
            WorkingDirectory = hostWorkspacePath,
            TimeoutSeconds = normalizeOptions.TimeoutSeconds
        };
    }

    private static ProcessCommand CreateDockerCheckCommand(
        string outputPath,
        PdfToolingOptions toolingOptions,
        int timeoutSeconds)
    {
        var hostWorkspacePath = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Output path must include a workspace directory.");

        var containerWorkspacePath = NormalizeContainerPath(toolingOptions.DockerContainerWorkspacePath);
        var containerOutputPath = $"{containerWorkspacePath}/{Path.GetFileName(outputPath)}";

        return new ProcessCommand
        {
            FileName = toolingOptions.DockerPath,
            Arguments =
            [
                "run",
                "--rm",
                "--network",
                "none",
                "-v",
                $"{hostWorkspacePath}:{containerWorkspacePath}",
                toolingOptions.QpdfDockerImage,
                "qpdf",
                "--check",
                containerOutputPath
            ],
            WorkingDirectory = hostWorkspacePath,
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static List<string> CreateQpdfArguments(
        string inputPath,
        string outputPath,
        PdfNormalizeOptions options)
    {
        var arguments = new List<string>();
        if (options.Linearize)
        {
            arguments.Add("--linearize");
        }
        else if (options.DisableObjectStreams)
        {
            arguments.Add("--object-streams=disable");
        }

        arguments.Add(inputPath);
        arguments.Add(outputPath);
        return arguments;
    }

    private static string NormalizeContainerPath(string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/work" : path.Trim();
        return normalized.TrimEnd('/');
    }
}
