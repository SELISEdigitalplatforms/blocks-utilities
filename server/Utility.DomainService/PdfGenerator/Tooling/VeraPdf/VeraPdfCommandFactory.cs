using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.VeraPdf;

public sealed class VeraPdfCommandFactory
{
    public ProcessCommand CreateValidateCommand(
        string inputPath,
        PdfToolingOptions toolingOptions)
    {
        if (toolingOptions.VeraPdfExecutionMode == VeraPdfExecutionMode.Docker)
        {
            return CreateDockerValidateCommand(inputPath, toolingOptions);
        }

        return new ProcessCommand
        {
            FileName = toolingOptions.VeraPdfPath,
            Arguments = ["--format", "xml", inputPath],
            WorkingDirectory = Path.GetDirectoryName(inputPath),
            TimeoutSeconds = toolingOptions.DefaultTimeoutSeconds
        };
    }

    public ProcessCommand CreateVersionCommand(PdfToolingOptions toolingOptions)
    {
        var timeoutSeconds = Math.Max(1, toolingOptions.DefaultTimeoutSeconds);

        if (toolingOptions.VeraPdfExecutionMode == VeraPdfExecutionMode.Docker)
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
                    toolingOptions.VeraPdfDockerImage,
                    "--version"
                ],
                TimeoutSeconds = timeoutSeconds
            };
        }

        return new ProcessCommand
        {
            FileName = toolingOptions.VeraPdfPath,
            Arguments = ["--version"],
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static ProcessCommand CreateDockerValidateCommand(
        string inputPath,
        PdfToolingOptions toolingOptions)
    {
        var hostWorkspacePath = Path.GetDirectoryName(inputPath)
            ?? throw new InvalidOperationException("Input path must include a workspace directory.");

        var containerWorkspacePath = NormalizeContainerPath(toolingOptions.DockerContainerWorkspacePath);
        var containerInputPath = $"{containerWorkspacePath}/{Path.GetFileName(inputPath)}";

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
                toolingOptions.VeraPdfDockerImage,
                "--format",
                "xml",
                containerInputPath
            ],
            WorkingDirectory = hostWorkspacePath,
            TimeoutSeconds = toolingOptions.DefaultTimeoutSeconds
        };
    }

    private static string NormalizeContainerPath(string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/work" : path.Trim();
        return normalized.TrimEnd('/');
    }
}
