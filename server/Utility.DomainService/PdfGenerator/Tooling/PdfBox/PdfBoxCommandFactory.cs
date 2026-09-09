using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;

namespace Utility.DomainService.PdfGenerator.Tooling.PdfBox;

public sealed class PdfBoxCommandFactory
{
    public const string FlattenMainClass = "PdfBoxFlatten";
    public const string NormalizeGeometryMainClass = "PdfBoxNormalizeGeometry";

    public ProcessCommand CreateFlattenCommand(
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        PdfToolingOptions options)
    {
        return CreateToolCommand(FlattenMainClass, inputPath, outputPath, timeoutSeconds, options);
    }

    public ProcessCommand CreateNormalizeGeometryCommand(
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        PdfToolingOptions options)
    {
        return CreateToolCommand(NormalizeGeometryMainClass, inputPath, outputPath, timeoutSeconds, options);
    }

    public ProcessCommand CreateVersionCommand(PdfToolingOptions options)
    {
        if (options.PdfBoxExecutionMode == PdfBoxExecutionMode.Docker)
        {
            return new ProcessCommand
            {
                FileName = options.DockerPath,
                Arguments =
                [
                    "run",
                    "--rm",
                    "--network",
                    "none",
                    options.PdfBoxDockerImage,
                    FlattenMainClass,
                    "--version"
                ],
                TimeoutSeconds = 10
            };
        }

        return new ProcessCommand
        {
            FileName = options.JavaPath,
            Arguments =
            [
                "-cp",
                $"{options.PdfBoxJarPath}{Path.PathSeparator}{options.PdfBoxClassPath}",
                FlattenMainClass,
                "--version"
            ],
            TimeoutSeconds = 10
        };
    }

    private static ProcessCommand CreateToolCommand(
        string mainClass,
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        PdfToolingOptions options)
    {
        if (options.PdfBoxExecutionMode == PdfBoxExecutionMode.Docker)
        {
            return CreateDockerCommand(mainClass, inputPath, outputPath, timeoutSeconds, options);
        }

        return new ProcessCommand
        {
            FileName = options.JavaPath,
            Arguments =
            [
                "-cp",
                $"{options.PdfBoxJarPath}{Path.PathSeparator}{options.PdfBoxClassPath}",
                mainClass,
                inputPath,
                outputPath
            ],
            WorkingDirectory = Path.GetDirectoryName(inputPath),
            TimeoutSeconds = timeoutSeconds
        };
    }

    public ProcessCommand CreateStandardizeCommand(
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        PdfToolingOptions options)
    {
        if (options.PdfBoxExecutionMode == PdfBoxExecutionMode.Docker)
        {
            return CreateDockerStandardizeCommand(inputPath, outputPath, timeoutSeconds, options);
        }

        return new ProcessCommand
        {
            FileName = options.JavaPath,
            Arguments =
            [
                "-cp",
                $"{options.PdfBoxJarPath}{Path.PathSeparator}{options.PdfBoxClassPath}",
                FlattenMainClass,
                "--standardize",
                inputPath,
                outputPath
            ],
            WorkingDirectory = Path.GetDirectoryName(inputPath),
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static ProcessCommand CreateDockerCommand(
        string mainClass,
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        PdfToolingOptions options)
    {
        var hostWorkspacePath = Path.GetDirectoryName(inputPath)
            ?? throw new InvalidOperationException("Input path must include a workspace directory.");
        var outputWorkspacePath = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Output path must include a workspace directory.");

        if (!Path.GetFullPath(hostWorkspacePath).Equals(
                Path.GetFullPath(outputWorkspacePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Docker PDFBox mode requires input and output files to be in the same operation workspace.");
        }

        var containerWorkspacePath = NormalizeContainerPath(options.DockerContainerWorkspacePath);
        var containerInputPath = $"{containerWorkspacePath}/{Path.GetFileName(inputPath)}";
        var containerOutputPath = $"{containerWorkspacePath}/{Path.GetFileName(outputPath)}";

        return new ProcessCommand
        {
            FileName = options.DockerPath,
            Arguments =
            [
                "run",
                "--rm",
                "--network",
                "none",
                "-v",
                $"{hostWorkspacePath}:{containerWorkspacePath}",
                options.PdfBoxDockerImage,
                mainClass,
                containerInputPath,
                containerOutputPath
            ],
            WorkingDirectory = hostWorkspacePath,
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static string NormalizeContainerPath(string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/work" : path.Trim();
        return normalized.TrimEnd('/');
    }

    private static ProcessCommand CreateDockerStandardizeCommand(
        string inputPath,
        string outputPath,
        int timeoutSeconds,
        PdfToolingOptions options)
    {
        var hostWorkspacePath = Path.GetDirectoryName(inputPath)
            ?? throw new InvalidOperationException("Input path must include a workspace directory.");
        var outputWorkspacePath = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("Output path must include a workspace directory.");

        if (!Path.GetFullPath(hostWorkspacePath).Equals(Path.GetFullPath(outputWorkspacePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Docker PDFBox mode requires input and output files to be in the same operation workspace.");
        }

        var containerWorkspacePath = NormalizeContainerPath(options.DockerContainerWorkspacePath);
        return new ProcessCommand
        {
            FileName = options.DockerPath,
            Arguments =
            [
                "run", "--rm", "--network", "none", "-v",
                $"{hostWorkspacePath}:{containerWorkspacePath}", options.PdfBoxDockerImage,
                FlattenMainClass,
                "--standardize",
                $"{containerWorkspacePath}/{Path.GetFileName(inputPath)}",
                $"{containerWorkspacePath}/{Path.GetFileName(outputPath)}"
            ],
            WorkingDirectory = hostWorkspacePath,
            TimeoutSeconds = timeoutSeconds
        };
    }
}
