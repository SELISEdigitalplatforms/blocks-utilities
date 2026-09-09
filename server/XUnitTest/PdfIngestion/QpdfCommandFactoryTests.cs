using FluentAssertions;
using Utility.DomainService.PdfGenerator.Tooling.Models;
using Utility.DomainService.PdfGenerator.Tooling.Options;
using Utility.DomainService.PdfGenerator.Tooling.Qpdf;

namespace XUnitTest.PdfIngestion;

/// <remarks>
/// Pure argument-construction logic - the highest-value tests in the toolchain, since a wrong flag
/// here silently changes what qpdf actually does to a customer's file, with no compiler or runtime
/// signal that anything is wrong.
/// </remarks>
public sealed class QpdfCommandFactoryTests
{
    private readonly QpdfCommandFactory _factory = new();

    [Fact]
    public void Direct_normalize_disables_object_streams_by_default()
    {
        var command = _factory.CreateNormalizeCommand(
            "/work/input.pdf", "/work/output.pdf", new PdfNormalizeOptions(), new PdfToolingOptions());

        command.FileName.Should().Be("/usr/bin/qpdf");
        command.Arguments.Should().ContainInOrder("--object-streams=disable", "/work/input.pdf", "/work/output.pdf");
    }

    [Fact]
    public void Direct_normalize_linearizes_instead_when_asked()
    {
        var options = new PdfNormalizeOptions { Linearize = true, DisableObjectStreams = true };

        var command = _factory.CreateNormalizeCommand("/work/in.pdf", "/work/out.pdf", options, new PdfToolingOptions());

        command.Arguments.Should().Contain("--linearize");
        command.Arguments.Should().NotContain("--object-streams=disable");
    }

    [Fact]
    public void Direct_check_command_targets_the_output_file()
    {
        var command = _factory.CreateCheckCommand("/work/out.pdf", new PdfToolingOptions(), timeoutSeconds: 30);

        command.FileName.Should().Be("/usr/bin/qpdf");
        command.Arguments.Should().Equal("--check", "/work/out.pdf");
        command.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void Direct_version_command_is_just_version()
    {
        var command = _factory.CreateVersionCommand(new PdfToolingOptions());

        command.FileName.Should().Be("/usr/bin/qpdf");
        command.Arguments.Should().Equal("--version");
    }

    [Fact]
    public void Docker_normalize_runs_the_configured_image_with_a_mounted_workspace()
    {
        var options = new PdfToolingOptions
        {
            QpdfExecutionMode = QpdfExecutionMode.Docker,
            DockerPath = "docker",
            DockerContainerWorkspacePath = "/work",
            QpdfDockerImage = "blocks-qpdf:dev"
        };

        // Built via Path.Combine, the same way PdfWorkspaceService builds real workspace paths,
        // so the assertion below matches on every OS rather than assuming forward slashes.
        var workspaceDir = Path.Combine(Path.GetTempPath(), "op-1");
        var inputPath = Path.Combine(workspaceDir, "input.pdf");
        var outputPath = Path.Combine(workspaceDir, "output.pdf");

        var command = _factory.CreateNormalizeCommand(inputPath, outputPath, new PdfNormalizeOptions(), options);

        command.FileName.Should().Be("docker");
        command.Arguments.Should().ContainInOrder("run", "--rm", "--network", "none", "-v", $"{workspaceDir}:/work", "blocks-qpdf:dev", "qpdf");
        command.Arguments.Should().Contain("/work/input.pdf");
        command.Arguments.Should().Contain("/work/output.pdf");
    }

    [Fact]
    public void Docker_normalize_refuses_input_and_output_in_different_workspaces()
    {
        var options = new PdfToolingOptions { QpdfExecutionMode = QpdfExecutionMode.Docker };

        var act = () => _factory.CreateNormalizeCommand(
            "/host/op-1/input.pdf", "/host/op-2/output.pdf", new PdfNormalizeOptions(), options);

        // Docker mode volume-mounts exactly one directory; an input/output pair split across two
        // workspaces would silently write nowhere the container can see.
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Docker_version_command_runs_qpdf_inside_the_image_rather_than_on_path()
    {
        var options = new PdfToolingOptions
        {
            QpdfExecutionMode = QpdfExecutionMode.Docker,
            QpdfDockerImage = "blocks-qpdf:dev"
        };

        var command = _factory.CreateVersionCommand(options);

        command.FileName.Should().Be("docker");
        command.Arguments.Should().ContainInOrder("blocks-qpdf:dev", "qpdf", "--version");
    }
}
